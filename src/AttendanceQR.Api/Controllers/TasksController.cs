using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The operator team's shared task board ("Tapşırıqlar"). GLOBAL, not per-tenant — access is a config
/// employee-id allowlist (App:TaskBoardEmployeeIds), like the super-admin panel. Everyone allowlisted
/// sees and edits the same one list, whichever company subdomain they are signed into.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Guid[] _allowed;

    public TasksController(AppDbContext db, AppOptions options)
    {
        _db = db;
        _allowed = options.TaskBoardIdList();
    }

    private bool CanAccess => _allowed.Contains(User.EmployeeId());
    private IActionResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, new { error = "NotAllowed" });

    /// <summary>Whether the caller may see the board — the frontend uses this to show/hide the menu.</summary>
    [HttpGet("access")]
    public IActionResult Access() => Ok(new { canAccess = CanAccess });

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!CanAccess) return Forbidden();
        var ct = HttpContext.RequestAborted;
        // Open first, in the manual drag order (SortOrder), newest as tiebreak; done items after.
        var items = await _db.Tasks
            .OrderBy(t => t.IsDone).ThenBy(t => t.SortOrder).ThenByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                id = t.Id,
                title = t.Title,
                isDone = t.IsDone,
                isImportant = t.IsImportant,
                dueDate = t.DueDate,
                by = t.CreatedByName,
                at = t.CreatedAtUtc,
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskCreateRequest request)
    {
        if (!CanAccess) return Forbidden();
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return BadRequest(new { error = "EmptyTitle" });
        if (title.Length > 500) title = title[..500];

        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        // The author may be in the current tenant; IgnoreQueryFilters keeps this robust regardless.
        var name = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == me).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "";

        // New tasks land at the TOP of the open list (Microsoft To Do adds newest first).
        var minOrder = await _db.Tasks.Where(t => !t.IsDone).Select(t => (double?)t.SortOrder).MinAsync(ct) ?? 0;
        var task = new TaskItem { Title = title, CreatedByEmployeeId = me, CreatedByName = name, SortOrder = minOrder - 1 };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = task.Id, by = name });
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id)
    {
        if (!CanAccess) return Forbidden();
        var ct = HttpContext.RequestAborted;
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFound(new { error = "NotFound" });
        task.IsDone = !task.IsDone;
        task.DoneAtUtc = task.IsDone ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { isDone = task.IsDone });
    }

    [HttpPost("{id:guid}/important")]
    public async Task<IActionResult> ToggleImportant(Guid id)
    {
        if (!CanAccess) return Forbidden();
        var ct = HttpContext.RequestAborted;
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFound(new { error = "NotFound" });
        task.IsImportant = !task.IsImportant;
        await _db.SaveChangesAsync(ct);
        return Ok(new { isImportant = task.IsImportant });
    }

    [HttpPut("{id:guid}/title")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] TaskTitleRequest request)
    {
        if (!CanAccess) return Forbidden();
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return BadRequest(new { error = "EmptyTitle" });
        if (title.Length > 500) title = title[..500];
        var ct = HttpContext.RequestAborted;
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFound(new { error = "NotFound" });
        task.Title = title;
        await _db.SaveChangesAsync(ct);
        return Ok(new { title = task.Title });
    }

    [HttpPut("{id:guid}/due")]
    public async Task<IActionResult> SetDue(Guid id, [FromBody] TaskDueRequest request)
    {
        if (!CanAccess) return Forbidden();
        var ct = HttpContext.RequestAborted;
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFound(new { error = "NotFound" });
        task.DueDate = request.DueDate;   // null clears it
        await _db.SaveChangesAsync(ct);
        return Ok(new { dueDate = task.DueDate });
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] TaskReorderRequest request)
    {
        if (!CanAccess) return Forbidden();
        var ct = HttpContext.RequestAborted;
        var ids = request.Ids ?? new List<Guid>();
        var tasks = await _db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        var pos = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => (double)x.i);
        foreach (var t in tasks)
            if (pos.TryGetValue(t.Id, out var order))
                t.SortOrder = order;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!CanAccess) return Forbidden();
        var ct = HttpContext.RequestAborted;
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is not null)
        {
            _db.Tasks.Remove(task);
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new { removed = task is not null });
    }
}
