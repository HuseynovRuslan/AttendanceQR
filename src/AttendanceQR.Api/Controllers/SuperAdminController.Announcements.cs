using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Platform-wide announcements: the operator broadcasts one message that lands on EVERY tenant's employees'
// home banner at once (see GlobalAnnouncement + AnnouncementsController.Active). Operator-only.
public partial class SuperAdminController
{
    // GET /api/super/announcements — the operator's broadcasts, newest first.
    [HttpGet("announcements")]
    public async Task<IActionResult> GlobalAnnouncements()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var rows = await _db.GlobalAnnouncements
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new
            {
                id = a.Id,
                title = a.Title,
                message = a.Message,
                isActive = a.IsActive,
                scheduledForUtc = a.ScheduledForUtc,
                createdByName = a.CreatedByName,
                createdAtUtc = a.CreatedAtUtc,
            })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    // POST /api/super/announcements — broadcast a new message to every company.
    [HttpPost("announcements")]
    public async Task<IActionResult> CreateGlobalAnnouncement([FromBody] GlobalAnnouncementRequest request)
    {
        if (!await CanAsync(OperatorPermission.Announce, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var title = request.Title?.Trim() ?? string.Empty;
        var message = request.Message?.Trim() ?? string.Empty;
        if (title.Length == 0 || message.Length == 0)
            return BadRequest(new { error = "TitleAndMessageRequired" });

        var actorId = User.EmployeeId();
        var actorName = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == actorId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "Operator";

        var a = new GlobalAnnouncement
        {
            Title = title,
            Message = message,
            // A past/near time means "live now"; a future time holds it until then (same rule the feed uses).
            ScheduledForUtc = request.ScheduledForUtc,
            IsActive = true,
            CreatedByEmployeeId = actorId,
            CreatedByName = actorName,
        };
        _db.GlobalAnnouncements.Add(a);
        await _db.SaveChangesAsync(ct);

        await AuditAsync("GlobalAnnouncementPosted", null, null, $"'{title}'", ct);
        return Ok(new { id = a.Id });
    }

    // POST /api/super/announcements/{id}/retire — stop showing it everywhere (kept, not deleted).
    [HttpPost("announcements/{id:guid}/retire")]
    public async Task<IActionResult> RetireGlobalAnnouncement(Guid id)
    {
        if (!await CanAsync(OperatorPermission.Announce, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var a = await _db.GlobalAnnouncements.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null)
            return NotFound(new { error = "NotFound" });

        a.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await AuditAsync("GlobalAnnouncementRetired", null, null, $"'{a.Title}'", ct);
        return Ok(new { id = a.Id, isActive = a.IsActive });
    }
}
