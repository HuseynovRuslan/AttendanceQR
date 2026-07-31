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
        // Open items first (newest first), done items after.
        var items = await _db.Tasks
            .OrderBy(t => t.IsDone).ThenByDescending(t => t.CreatedAtUtc)
            .Select(t => new { id = t.Id, title = t.Title, isDone = t.IsDone, by = t.CreatedByName, at = t.CreatedAtUtc })
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

        var task = new TaskItem { Title = title, CreatedByEmployeeId = me, CreatedByName = name };
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
