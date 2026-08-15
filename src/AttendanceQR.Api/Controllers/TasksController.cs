using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// A company's own shared task board — «Tapşırıqlar». Every admin and manager of the company shares
/// one list; the tenant query filter is what keeps one company's board out of another's.
///
/// It was global and allowlisted until 2026-08-15. Opening it to a whole company could not be done
/// on a global table without showing every customer's admin the operator team's own roadmap, so the
/// table was tenant-scoped first and the allowlist dropped second. See <see cref="TaskItem"/>.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/tasks")]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _db;

    public TasksController(AppDbContext db)
    {
        _db = db;
    }

    private bool CanAccess => true;
    private IActionResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden, new { error = "NotAllowed" });

    /// <summary>Kept so an older cached frontend still gets an answer it understands; every
    /// admin/manager may now see the board, so it always says yes.</summary>
    [HttpGet("access")]
    public IActionResult Access() => Ok(new { canAccess = true });

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
                assignedToEmployeeId = t.AssignedToEmployeeId,
                assignedToName = t.AssignedToName,
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

    /// <summary>Give a task to a teammate. The candidates are the company's own admins and managers —
    /// this is the team's board, and putting an item on it for someone who cannot open it would be a
    /// message sent nowhere.</summary>
    [HttpPut("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] TaskAssignRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return NotFound(new { error = "NotFound" });

        if (request.EmployeeId is not Guid target)
        {
            task.AssignedToEmployeeId = null;
            task.AssignedToName = null;
        }
        else
        {
            // The tenant filter already confines this to the caller's company; the role check is what
            // stops a task being handed to somebody with no way to see it.
            var person = await _db.Employees.FirstOrDefaultAsync(
                e => e.Id == target && e.IsActive
                     && (e.Role == EmployeeRole.Admin || e.Role == EmployeeRole.Manager), ct);
            if (person is null) return BadRequest(new { error = "NotAssignable" });

            task.AssignedToEmployeeId = person.Id;
            task.AssignedToName = person.FullName;
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { assignedToEmployeeId = task.AssignedToEmployeeId, assignedToName = task.AssignedToName });
    }

    /// <summary>Who a task may be given to — the company's admins and managers, for the picker.</summary>
    [HttpGet("assignable")]
    public async Task<IActionResult> Assignable()
    {
        var ct = HttpContext.RequestAborted;
        var people = await _db.Employees
            .Where(e => e.IsActive && (e.Role == EmployeeRole.Admin || e.Role == EmployeeRole.Manager))
            .OrderBy(e => e.FullName)
            .Select(e => new { id = e.Id, name = e.FullName })
            .ToListAsync(ct);
        return Ok(people);
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
