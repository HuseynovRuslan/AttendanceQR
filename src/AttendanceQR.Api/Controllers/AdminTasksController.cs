using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Tapşırıqlar — a rəhbər (Admin, or a Manager) assigns a task to a Manager/field supervisor, who
/// later marks it done. Two notifications, one per state change:
///   • on assign    → the assignee is told "sizə yeni tapşırıq verildi"
///   • on complete  → the assigner is told "tapşırıq tamamlandı"
/// Each is a best-effort Web Push (no-op when the recipient has no subscription) plus a live bell
/// item computed in AdminNotificationsController from this table — there is no persisted inbox row.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/tasks")]
public class AdminTasksController : ControllerBase
{
    private const int MaxTitle = 200;

    private readonly AppDbContext _db;
    private readonly IPushNotifier _notifier;

    public AdminTasksController(AppDbContext db, IPushNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var me = User.EmployeeId();
        var ct = HttpContext.RequestAborted;

        // Everything I gave out or that was given to me. Query filter already scopes to my tenant.
        var tasks = await _db.Tasks
            .Where(t => t.AssignedByEmployeeId == me || t.AssignedToEmployeeId == me)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);

        var names = await _db.Employees
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        return Ok(tasks.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            description = t.Description,
            dueDate = t.DueDate,
            status = t.Status.ToString(),
            direction = t.AssignedByEmployeeId == me ? "outgoing" : "incoming",
            assignedById = t.AssignedByEmployeeId,
            assignedByName = names.GetValueOrDefault(t.AssignedByEmployeeId, "—"),
            assignedToId = t.AssignedToEmployeeId,
            assignedToName = names.GetValueOrDefault(t.AssignedToEmployeeId, "—"),
            createdAtUtc = t.CreatedAtUtc,
            completedAtUtc = t.CompletedAtUtc,
            acknowledged = t.AcknowledgedByAssignerAtUtc != null,
        }));
    }

    /// <summary>What the current user may do with tasks, used to gate the nav item and the assign form.
    /// Admins may assign to anyone; a granted assigner only to their allowed recipients.</summary>
    [HttpGet("access")]
    public async Task<IActionResult> Access()
    {
        var me = User.EmployeeId();
        var ct = HttpContext.RequestAborted;
        var (_, isGiver, allowedIds) = await ResolveAccessAsync(me, ct);
        var isRecipient = await _db.TaskAssignPermissions.AnyAsync(p => p.RecipientEmployeeId == me, ct);

        var names = allowedIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Employees.Where(e => allowedIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var recipients = allowedIds
            .Select(id => new { id, name = names.GetValueOrDefault(id, "—") })
            .OrderBy(r => r.name)
            .ToList();

        return Ok(new { canSee = isGiver || isRecipient, canAssign = isGiver, recipients });
    }

    // (isAdmin, isGiver, allowedRecipientIds). Admin may assign to any active employee; a non-admin may
    // assign only to the recipients an admin has explicitly granted them.
    private async Task<(bool isAdmin, bool isGiver, HashSet<Guid> allowedRecipientIds)> ResolveAccessAsync(
        Guid me, CancellationToken ct)
    {
        if (User.Role() == EmployeeRole.Admin)
        {
            var all = await _db.Employees.Where(e => e.IsActive && e.Id != me).Select(e => e.Id).ToListAsync(ct);
            return (true, true, all.ToHashSet());
        }
        var allowed = await _db.TaskAssignPermissions
            .Where(p => p.AssignerEmployeeId == me)
            .Select(p => p.RecipientEmployeeId)
            .ToListAsync(ct);
        return (false, allowed.Count > 0, allowed.ToHashSet());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTaskRequest request)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
            return BadRequest(new { error = "TitleRequired" });
        if (title.Length > MaxTitle)
            return BadRequest(new { error = "TitleTooLong" });
        if (request.DueDate is null)
            return BadRequest(new { error = "DueDateRequired" });

        var me = User.EmployeeId();
        var ct = HttpContext.RequestAborted;
        var (_, isGiver, allowedIds) = await ResolveAccessAsync(me, ct);
        if (!isGiver)
            return StatusCode(403, new { error = "NotAllowedToAssign" });

        var targets = (request.AssignedToEmployeeIds ?? Array.Empty<Guid>())
            .Where(id => id != me)
            .Distinct()
            .ToArray();
        if (targets.Length == 0)
            return BadRequest(new { error = "NoRecipients" });
        if (targets.Any(id => !allowedIds.Contains(id)))
            return StatusCode(403, new { error = "RecipientNotAllowed" });

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var tasks = targets.Select(id => new TaskItem
        {
            AssignedByEmployeeId = me,
            AssignedToEmployeeId = id,
            Title = title,
            Description = description,
            DueDate = request.DueDate,
            Status = TaskItemStatus.Pending,
        }).ToList();
        _db.Tasks.AddRange(tasks);
        await _db.SaveChangesAsync();

        // Best-effort — a check-in is never blocked by notifications, and neither is assigning a task.
        await _notifier.NotifyEmployeesAsync(targets, "Yeni tapşırıq", title, "/admin/tasks", ct);

        return Ok(new { ids = tasks.Select(t => t.Id).ToArray() });
    }

    /// <summary>The assignee marks their own task done. Only the assignee may complete it.</summary>
    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var me = User.EmployeeId();
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);
        if (task is null)
            return NotFound(new { error = "NotFound" });
        if (task.AssignedToEmployeeId != me)
            return StatusCode(403, new { error = "NotYourTask" });
        if (task.Status == TaskItemStatus.Completed)
            return Ok(new { id = task.Id, status = task.Status.ToString() }); // idempotent

        task.Status = TaskItemStatus.Completed;
        task.CompletedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var doerName = await _db.Employees
            .Where(e => e.Id == me).Select(e => e.FullName)
            .FirstOrDefaultAsync(HttpContext.RequestAborted) ?? "İşçi";

        await _notifier.NotifyEmployeesAsync(
            new[] { task.AssignedByEmployeeId },
            "Tapşırıq tamamlandı",
            $"{doerName}: {task.Title}",
            "/admin/tasks",
            HttpContext.RequestAborted);

        return Ok(new { id = task.Id, status = task.Status.ToString() });
    }

    /// <summary>The assigner acknowledges a completion, clearing it from their bell.</summary>
    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var me = User.EmployeeId();
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);
        if (task is null)
            return NotFound(new { error = "NotFound" });
        if (task.AssignedByEmployeeId != me)
            return StatusCode(403, new { error = "NotYourTask" });

        if (task.AcknowledgedByAssignerAtUtc is null)
        {
            task.AcknowledgedByAssignerAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return Ok(new { id = task.Id });
    }

    /// <summary>The assigner deletes/cancels a task they created.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var me = User.EmployeeId();
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);
        if (task is null)
            return NotFound(new { error = "NotFound" });
        if (task.AssignedByEmployeeId != me)
            return StatusCode(403, new { error = "NotYourTask" });

        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }
}
