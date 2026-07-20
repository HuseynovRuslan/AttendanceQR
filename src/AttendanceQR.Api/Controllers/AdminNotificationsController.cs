using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin bell-icon notifications — computed live from existing data on every call, no persisted
/// Notification entity, no read/unread state. Two triggers for now: a pending device-change
/// request (one item each, individually actionable) and today's late count (one summarized item
/// linking to the live board). Always reflects current state, so there's nothing to "dismiss" —
/// approving a device change or the day rolling over naturally drops the count on the next poll.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/notifications")]
public class AdminNotificationsController : ControllerBase
{
    // Bounds the dropdown if pending requests ever pile up — the badge's totalCount still reflects
    // the true number, only the itemized list is capped.
    private const int MaxPendingItems = 20;

    private readonly IDeviceChangeService _deviceChangeService;
    private readonly IReportQueryService _reports;
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public AdminNotificationsController(
        IDeviceChangeService deviceChangeService, IReportQueryService reports, AppDbContext db, AppOptions options)
    {
        _deviceChangeService = deviceChangeService;
        _reports = reports;
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var requesterId = User.EmployeeId();

        var pending = await _deviceChangeService.GetPendingAsync(HttpContext.RequestAborted);

        // No "N employees were late today": every employee keeps their own hours, so a location-wide
        // shift cannot decide who was late — the alert was simply wrong, every morning.
        var items = pending
            .Take(MaxPendingItems)
            .Select(p => new
            {
                type = "PendingDeviceChange",
                message = $"{p.EmployeeName} — yeni cihaz təsdiqi gözləyir",
                linkTo = "/admin/device-changes"
            })
            .ToList<object>();

        var birthdays = await BirthdayItemsAsync();
        items.AddRange(birthdays);

        return Ok(new
        {
            // Birthdays count toward the badge so the reminder is actually noticed.
            totalCount = pending.Count + birthdays.Count,
            items
        });
    }

    // Today's and tomorrow's birthdays, as bell reminders ("Sabah Əlinin doğum günüdür"). Only
    // employees with a full BirthDate; matched on day/month in local (Baku) time.
    private async Task<List<object>> BirthdayItemsAsync()
    {
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        var tomorrow = todayLocal.AddDays(1);

        var withDob = await _db.Employees
            .Where(e => e.IsActive && e.BirthDate != null)
            .Select(e => new { e.FullName, Dob = e.BirthDate!.Value })
            .ToListAsync(HttpContext.RequestAborted);

        var result = new List<object>();
        foreach (var e in withDob.OrderBy(e => e.FullName))
        {
            if (e.Dob.Month == todayLocal.Month && e.Dob.Day == todayLocal.Day)
                result.Add(new { type = "Birthday", message = $"🎂 Bu gün {e.FullName}-in doğum günüdür!", linkTo = "/admin/birthdays" });
            else if (e.Dob.Month == tomorrow.Month && e.Dob.Day == tomorrow.Day)
                result.Add(new { type = "Birthday", message = $"🎂 Sabah {e.FullName}-in doğum günüdür", linkTo = "/admin/birthdays" });
        }
        return result;
    }
}
