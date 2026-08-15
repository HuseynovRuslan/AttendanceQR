using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>A manager assigning a job to one worker.</summary>
public sealed record AssignTaskRequest(Guid EmployeeId, string Title, string? Description, DateOnly? DueDate);

/// <summary>The worker pressing «Hazırdır» — an optional note, and an optional proof photo.</summary>
public sealed record CompleteTaskRequest(string? Note, string? PhotoBase64);

/// <summary>The manager sending a task back with a reason.</summary>
public sealed record RejectTaskRequest(string? Note);

/// <summary>
/// «Tapşırıqlar» — jobs a manager gives a worker, and the worker's «Hazırdır».
///
/// Two audiences behind one route: <c>/mine</c> is the worker's own list and is the only place a
/// worker can touch anything; everything else is Admin/Manager and goes through
/// <see cref="LocationScopeRules"/>, so a branch manager can only ever assign to, and see, their own
/// branch's staff — never an admin, never a fellow manager, never another branch.
///
/// The house rule that shapes the write paths: a task is never blocked by anything optional. A photo
/// that fails to upload, a push that cannot be delivered — neither may stop «Hazırdır» from being
/// recorded, because the worker HAS done the work and the record of that is the point.
/// </summary>
[ApiController]
[Authorize]
[RequireFeature(TenantFeatures.EmployeeTasks)]
[Route("api/employee-tasks")]
public class EmployeeTasksController : ControllerBase
{
    private const int MaxTitle = 200;
    private const int MaxDescription = 2000;
    private const int MaxNote = 1000;
    /// <summary>How far back the worker's list reaches for FINISHED tasks. Open ones are always shown,
    /// however old — an overdue job does not stop mattering because a fortnight passed.</summary>
    private const int DoneHistoryDays = 14;

    private readonly AppDbContext _db;
    private readonly IPhotoStorageService _photos;
    private readonly IPushNotifier _push;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<EmployeeTasksController> _logger;

    public EmployeeTasksController(
        AppDbContext db, IPhotoStorageService photos, IPushNotifier push, AppOptions options,
        ILogger<EmployeeTasksController> logger)
    {
        _db = db;
        _photos = photos;
        _push = push;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _logger = logger;
    }

    private Guid Me => User.EmployeeId();
    private DateOnly TodayLocal() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

    // ---------------------------------------------------------------- worker ----

    /// <summary>GET /api/employee-tasks/mine — everything still open, plus what was finished recently.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var ct = HttpContext.RequestAborted;
        var since = DateTime.UtcNow.AddDays(-DoneHistoryDays);

        var tasks = await _db.EmployeeTasks
            .Where(t => t.EmployeeId == Me
                        && t.Status != EmployeeTaskStatus.Cancelled
                        && (t.Status == EmployeeTaskStatus.Assigned || t.DoneAtUtc >= since))
            .OrderBy(t => t.Status)
            .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
            .ThenBy(t => t.AssignedAtUtc)
            .ToListAsync(ct);

        return Ok(tasks.Select(t => Project(t, TodayLocal())));
    }

    /// <summary>POST /api/employee-tasks/{id}/done — «Hazırdır».</summary>
    [HttpPost("{id:guid}/done")]
    public async Task<IActionResult> Complete(Guid id, [FromBody] CompleteTaskRequest req)
    {
        var ct = HttpContext.RequestAborted;

        // Only the task's OWN worker. A manager marking someone else's work done would make the
        // record a claim about a person who never said it.
        var task = await _db.EmployeeTasks.FirstOrDefaultAsync(t => t.Id == id && t.EmployeeId == Me, ct);
        if (task is null)
            return NotFound(new { error = "TaskNotFound" });
        if (task.Status == EmployeeTaskStatus.Cancelled)
            return BadRequest(new { error = "TaskCancelled" });

        // Already done? Answer OK with the original time. The reply to the first call is easily lost
        // (a 502 mid-deploy, a handover between wifi and LTE) and the app then retries; refusing the
        // retry would tell a worker who HAS finished that they had not.
        if (task.Status is EmployeeTaskStatus.Done or EmployeeTaskStatus.Approved)
            return Ok(Project(task, TodayLocal()));

        // 1. The record itself, committed alone before anything optional runs.
        task.Status = EmployeeTaskStatus.Done;
        task.DoneAtUtc = DateTime.UtcNow;
        task.WorkerNote = Trim(req.Note, MaxNote);
        task.RejectionNote = null; // a redo clears the old complaint
        await _db.SaveChangesAsync(ct);

        // 2. The photo — best effort, and never the reason «Hazırdır» fails.
        if (!string.IsNullOrWhiteSpace(req.PhotoBase64))
        {
            try
            {
                var bytes = DecodeImage(req.PhotoBase64);
                if (bytes.Length is > 0 and <= 4 * 1024 * 1024)
                {
                    task.PhotoKey = await _photos.UploadTaskPhotoAsync(_db.CurrentTenantId, task.Id, bytes, ct);
                    task.PhotoAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Task {TaskId}: proof photo not stored", task.Id);
            }
        }

        // 3. Tell the manager who asked for it.
        try
        {
            await _push.NotifyEmployeesAsync(
                new[] { task.AssignedByEmployeeId }, "Tapşırıq hazırdır",
                $"{task.Title} — icraçı tapşırığı bitirdi.", "/admin/employee-tasks", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task {TaskId}: completion push failed", task.Id);
        }

        return Ok(Project(task, TodayLocal()));
    }

    // ------------------------------------------------------- manager / admin ----

    /// <summary>GET /api/employee-tasks — the board, scoped to what the caller may manage.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Board([FromQuery] string? status)
    {
        var ct = HttpContext.RequestAborted;
        var query = _db.EmployeeTasks.AsQueryable();

        // A manager sees only their own branches' staff. Admin sees the whole company.
        if (User.Role() == EmployeeRole.Manager)
        {
            var locationIds = await LocationScopeRules.ManagedLocationIdsAsync(_db, Me, ct);
            var employeeIds = await _db.Employees
                .Where(e => locationIds.Contains(e.LocationId)).Select(e => e.Id).ToListAsync(ct);
            query = query.Where(t => employeeIds.Contains(t.EmployeeId));
        }

        if (Enum.TryParse<EmployeeTaskStatus>(status, ignoreCase: true, out var wanted))
            query = query.Where(t => t.Status == wanted);

        var tasks = await query
            .OrderBy(t => t.Status)
            .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(t => t.AssignedAtUtc)
            .Take(500)
            .ToListAsync(ct);

        var ids = tasks.Select(t => t.EmployeeId).Concat(tasks.Select(t => t.AssignedByEmployeeId)).Distinct().ToList();
        var names = await _db.Employees.Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var today = TodayLocal();
        return Ok(tasks.Select(t => Project(t, today, names)));
    }

    /// <summary>POST /api/employee-tasks — assign a job to one worker.</summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Assign([FromBody] AssignTaskRequest req)
    {
        var ct = HttpContext.RequestAborted;

        var title = Trim(req.Title, MaxTitle);
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { error = "TitleRequired" });

        var worker = await _db.Employees.FirstOrDefaultAsync(e => e.Id == req.EmployeeId && e.IsActive, ct);
        if (worker is null)
            return BadRequest(new { error = "EmployeeNotFound" });

        // The management rule, not the looser visibility one: a manager may assign only to a
        // Role==Employee worker in their own branches.
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), req.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var task = new EmployeeTask
        {
            EmployeeId = req.EmployeeId,
            AssignedByEmployeeId = Me,
            Title = title,
            Description = Trim(req.Description, MaxDescription),
            DueDate = req.DueDate,
        };
        _db.EmployeeTasks.Add(task);
        await _db.SaveChangesAsync(ct);

        // Best effort: an unreachable phone must not undo an assignment the manager already made.
        try
        {
            var due = task.DueDate is DateOnly d ? $" · son tarix {d:dd.MM}" : string.Empty;
            await _push.NotifyEmployeesAsync(
                new[] { task.EmployeeId }, "Yeni tapşırıq", $"{task.Title}{due}", "/tasks", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task {TaskId}: assignment push failed", task.Id);
        }

        return Ok(Project(task, TodayLocal()));
    }

    /// <summary>POST /api/employee-tasks/{id}/approve — the manager accepts the work.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Approve(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var (task, denied) = await ManageableTaskAsync(id, ct);
        if (denied is not null) return denied;
        if (task!.Status != EmployeeTaskStatus.Done)
            return BadRequest(new { error = "NotDone" });

        task.Status = EmployeeTaskStatus.Approved;
        task.ApprovedByEmployeeId = Me;
        task.ApprovedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(Project(task, TodayLocal()));
    }

    /// <summary>POST /api/employee-tasks/{id}/reject — send it back to be redone, with a reason.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectTaskRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var (task, denied) = await ManageableTaskAsync(id, ct);
        if (denied is not null) return denied;
        if (task!.Status != EmployeeTaskStatus.Done)
            return BadRequest(new { error = "NotDone" });

        task.Status = EmployeeTaskStatus.Assigned;
        task.DoneAtUtc = null;
        task.RejectionNote = Trim(req.Note, MaxNote);
        await _db.SaveChangesAsync(ct);

        try
        {
            await _push.NotifyEmployeesAsync(
                new[] { task.EmployeeId }, "Tapşırıq geri qaytarıldı",
                task.RejectionNote is { Length: > 0 } n ? $"{task.Title} — {n}" : task.Title, "/tasks", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Task {TaskId}: rejection push failed", task.Id);
        }

        return Ok(Project(task, TodayLocal()));
    }

    /// <summary>POST /api/employee-tasks/{id}/cancel — call it off. Kept, not deleted: the worker has
    /// already seen it, and a job that vanishes without trace is the one they will ask about.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var (task, denied) = await ManageableTaskAsync(id, ct);
        if (denied is not null) return denied;

        task!.Status = EmployeeTaskStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return Ok(Project(task, TodayLocal()));
    }

    /// <summary>GET /api/employee-tasks/{id}/photo — a short-lived URL for the proof photo.</summary>
    [HttpGet("{id:guid}/photo")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Photo(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var (task, denied) = await ManageableTaskAsync(id, ct);
        if (denied is not null) return denied;
        if (task!.PhotoKey is null)
            return Ok(new { url = (string?)null });

        var url = await _photos.GetPresignedUrlAsync(task.PhotoKey, ct);
        return Ok(new { url });
    }

    // ------------------------------------------------------------------ bits ----

    /// <summary>The task, if the caller may manage the worker it belongs to. One place, so no endpoint
    /// can forget the branch rule.</summary>
    private async Task<(EmployeeTask?, IActionResult?)> ManageableTaskAsync(Guid id, CancellationToken ct)
    {
        var task = await _db.EmployeeTasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
            return (null, NotFound(new { error = "TaskNotFound" }));
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), task.EmployeeId, ct))
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" }));
        return (task, null);
    }

    private static string? Trim(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private static byte[] DecodeImage(string input)
    {
        var comma = input.IndexOf(',');
        var b64 = input.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? input[(comma + 1)..]
            : input;
        try { return Convert.FromBase64String(b64); }
        catch (FormatException) { return Array.Empty<byte>(); }
    }

    /// <summary>Overdue is computed here against TODAY, never stored — a flag written by a nightly job
    /// would be a day stale every morning, which is precisely when it matters.</summary>
    private static object Project(EmployeeTask t, DateOnly today, IReadOnlyDictionary<Guid, string>? names = null) => new
    {
        id = t.Id,
        title = t.Title,
        description = t.Description,
        dueDate = t.DueDate,
        status = t.Status.ToString(),
        overdue = t.Status == EmployeeTaskStatus.Assigned && t.DueDate is DateOnly d && d < today,
        assignedAtUtc = t.AssignedAtUtc,
        doneAtUtc = t.DoneAtUtc,
        approvedAtUtc = t.ApprovedAtUtc,
        workerNote = t.WorkerNote,
        rejectionNote = t.RejectionNote,
        hasPhoto = t.PhotoKey != null,
        employeeId = t.EmployeeId,
        employeeName = names?.GetValueOrDefault(t.EmployeeId),
        assignedByName = names?.GetValueOrDefault(t.AssignedByEmployeeId),
    };
}
