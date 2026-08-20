using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Employee-facing read of the tenant's announcements — home banner + notifications. Filters by
/// schedule (only once due) and audience (All / at-work-today / not-at-work-today / an explicit list),
/// evaluated for the signed-in employee. Tenant-scoped.
/// </summary>
[ApiController]
[Authorize]
[Route("api/announcements")]
public class AnnouncementsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnnouncementsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Active()
    {
        var ct = HttpContext.RequestAborted;
        var nowUtc = DateTime.UtcNow;
        var employeeId = User.EmployeeId();

        // Platform-wide operator broadcasts reach EVERY tenant's employees, regardless of the tenant's own
        // announcements feature flag (they are operator-level messages — e.g. planned maintenance).
        var merged = (await _db.GlobalAnnouncements
                .Where(a => a.IsActive && (a.ScheduledForUtc == null || a.ScheduledForUtc <= nowUtc))
                .Select(a => new { a.Id, a.Title, a.Message, a.CreatedAtUtc })
                .ToListAsync(ct))
            .Select(a => new { id = a.Id, title = a.Title, message = a.Message, createdAtUtc = a.CreatedAtUtc, global = true })
            .ToList();

        // The tenant's OWN announcements — skipped entirely when the plan turns the feature off (the
        // employee still gets any global broadcast above, just none of this company's own).
        var disabled = await _db.Tenants
            .Where(t => t.Id == _db.CurrentTenantId)
            .Select(t => t.DisabledFeatures)
            .FirstOrDefaultAsync(ct);

        if (TenantFeatures.IsEnabled(disabled, TenantFeatures.Announcements))
        {
            // AttendanceRecords are keyed by the server UTC day (see the scan handler), so match that.
            var todayUtc = DateOnly.FromDateTime(nowUtc);
            var atWorkToday = await _db.AttendanceRecords
                .AnyAsync(r => r.EmployeeId == employeeId && r.AttendanceDate == todayUtc && r.CheckInAtUtc != null, ct);

            var due = await _db.Announcements
                .Where(a => a.IsActive && (a.ScheduledForUtc == null || a.ScheduledForUtc <= nowUtc))
                .OrderByDescending(a => a.CreatedAtUtc)
                .Select(a => new { a.Id, a.Title, a.Message, a.CreatedAtUtc, a.Audience })
                .ToListAsync(ct);

            // For any "Selected" ones, which of them list THIS employee?
            var selectedIds = due.Where(a => a.Audience == AnnouncementAudience.Selected).Select(a => a.Id).ToList();
            var mineSelected = selectedIds.Count == 0
                ? new HashSet<Guid>()
                : (await _db.AnnouncementRecipients
                        .Where(r => selectedIds.Contains(r.AnnouncementId) && r.EmployeeId == employeeId)
                        .Select(r => r.AnnouncementId)
                        .ToListAsync(ct))
                    .ToHashSet();

            merged.AddRange(due
                .Where(a => a.Audience switch
                {
                    AnnouncementAudience.All => true,
                    AnnouncementAudience.AtWork => atWorkToday,
                    AnnouncementAudience.NotAtWork => !atWorkToday,
                    AnnouncementAudience.Selected => mineSelected.Contains(a.Id),
                    _ => true,
                })
                .Select(a => new { id = a.Id, title = a.Title, message = a.Message, createdAtUtc = a.CreatedAtUtc, global = false }));
        }

        var result = merged.OrderByDescending(x => x.createdAtUtc).ToList();
        return Ok(result);
    }
}

/// <summary>Admin management of announcements — post (with title, audience, optional schedule), list,
/// retire, delete.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/announcements")]
[RequireFeature(TenantFeatures.Announcements)]
public class AdminAnnouncementsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;
    private readonly IAnnouncementPushQueue _pushQueue;

    public AdminAnnouncementsController(AppDbContext db, AppOptions options, IAnnouncementPushQueue pushQueue)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _pushQueue = pushQueue;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.Announcements
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new
            {
                id = a.Id,
                title = a.Title,
                message = a.Message,
                audience = a.Audience.ToString(),
                scheduledForUtc = a.ScheduledForUtc,
                recipientCount = a.Recipients.Count,
                isActive = a.IsActive,
                createdAtUtc = a.CreatedAtUtc,
            })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AnnouncementRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { error = "MessageRequired" });
        if (message.Length > 2000)
            return BadRequest(new { error = "MessageTooLong" });

        if (!Enum.TryParse<AnnouncementAudience>(request.Audience, ignoreCase: true, out var audience))
            audience = AnnouncementAudience.All;

        // "Planlaşdır": a wall-clock time in the app timezone → UTC. Null / unparseable = show now.
        DateTime? scheduledUtc = null;
        if (!string.IsNullOrWhiteSpace(request.ScheduledForLocal)
            && DateTime.TryParse(request.ScheduledForLocal, out var local))
        {
            scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _timeZone);
        }

        var announcement = new Announcement
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim(),
            Message = message,
            Audience = audience,
            ScheduledForUtc = scheduledUtc,
        };

        if (audience == AnnouncementAudience.Selected && request.RecipientIds is { Count: > 0 })
        {
            foreach (var empId in request.RecipientIds.Distinct())
                announcement.Recipients.Add(new AnnouncementRecipient { EmployeeId = empId });
        }

        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync(ct);

        // The push goes out via AnnouncementPushWorker, never inside this request: a 2000-employee
        // fan-out took minutes here and died with the browser's patience (RequestAborted cancelled it
        // partway — some of the company notified, the rest never). An unscheduled announcement is
        // queued now; a scheduled one is found by the worker's sweep when it comes due.
        if (scheduledUtc is null)
            _pushQueue.Enqueue(_db.CurrentTenantId, announcement.Id);

        return Ok(new { id = announcement.Id });
    }

    // Retire (soft-delete) so it disappears for employees without vanishing from the admin's history.
    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id)
    {
        var announcement = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id, HttpContext.RequestAborted);
        if (announcement is null)
            return NotFound(new { error = "AnnouncementNotFound" });
        announcement.IsActive = false;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { id, isActive = false });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var announcement = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (announcement is null)
            return NotFound(new { error = "AnnouncementNotFound" });

        var recipients = await _db.AnnouncementRecipients.Where(r => r.AnnouncementId == id).ToListAsync(ct);
        _db.AnnouncementRecipients.RemoveRange(recipients);
        _db.Announcements.Remove(announcement);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = id });
    }
}
