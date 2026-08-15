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

    // GET /api/admin/notifications/badges — the sidebar's shelf counts: how many requests WAIT on
    // the admin (device changes, PIN resets) and how many past days sit open. Separate from the bell
    // payload on purpose: the bell speaks sentences ("3 PIN sıfırlama tələbi gözləyir") and a badge
    // that parsed numbers back out of Azerbaijani prose would break on the first rewording.
    [HttpGet("badges")]
    public async Task<IActionResult> Badges()
    {
        var ct = HttpContext.RequestAborted;
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        var deviceChanges = (await _deviceChangeService.GetPendingAsync(ct)).Count;
        var pinResets = await _db.PinResetRequests.CountAsync(p => p.Status == PinResetStatus.Pending, ct);
        // Today excluded — an open record today is just someone still at work.
        var openRecords = await _db.AttendanceRecords
            .CountAsync(r => r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < todayLocal, ct);

        // Open team-board items past their date. The board is the only place these show today, and a
        // deadline nobody is reminded of is a deadline that quietly passes — which is exactly what the
        // 16 items on it had been doing.
        var overdueTasks = await _db.Tasks.CountAsync(t => !t.IsDone && t.DueDate != null && t.DueDate < todayLocal, ct);

        return Ok(new { deviceChanges, pinResets, openRecords, overdueTasks });
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ct = HttpContext.RequestAborted;

        // ---- Needs-attention items (drive the badge) ----
        var pending = await _deviceChangeService.GetPendingAsync(ct);
        var items = pending
            .Take(MaxPendingItems)
            .Select(p => new
            {
                type = "PendingDeviceChange",
                message = $"{p.EmployeeName} — yeni cihaz təsdiqi gözləyir",
                linkTo = "/admin/device-changes",
            })
            .ToList<object>();

        // Pending PIN-reset requests (a headless employee waiting to get back in).
        var pinPending = await _db.PinResetRequests.CountAsync(p => p.Status == PinResetStatus.Pending, ct);
        if (pinPending > 0)
            items.Add(new { type = "PendingPinReset", message = $"{pinPending} PIN sıfırlama tələbi gözləyir", linkTo = "/admin/pin-resets" });

        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        // Today's rejected / blocked scans (the Problems screen). Start-of-day in Baku, as a UTC instant.
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(todayLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), _timeZone);
        var rejectedTypes = new[] { AuditEventType.CheckInRejected, AuditEventType.CheckOutRejected, AuditEventType.ScanBlockedOnDevice };
        var problemsToday = await _db.AuditLogs.CountAsync(a => a.CreatedAtUtc >= todayStartUtc && rejectedTypes.Contains(a.EventType), ct);
        if (problemsToday > 0)
            items.Add(new { type = "Problems", message = $"{problemsToday} rədd edilmiş skan (bu gün)", linkTo = "/admin/problems" });

        // Past days someone checked in but never out (today is excluded — that's just "still at work").
        var openRecords = await _db.AttendanceRecords
            .CountAsync(r => r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < todayLocal, ct);
        if (openRecords > 0)
            items.Add(new { type = "OpenRecords", message = $"{openRecords} gün çıxış edilməyib", linkTo = "/admin/open-records" });

        var birthdays = await BirthdayItemsAsync();
        items.AddRange(birthdays);

        // ---- Recent activity (informational — today's latest check-ins / check-outs, office AND field).
        // Does NOT inflate the badge (that would run to hundreds); the admin opens the bell to glance. ----
        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == todayLocal && (r.CheckInAtUtc != null || r.CheckOutAtUtc != null))
            .Select(r => new { r.EmployeeId, r.CheckInAtUtc, r.CheckOutAtUtc })
            .ToListAsync(ct);
        var field = await _db.FieldVisits
            .Where(v => v.VisitDate == todayLocal && (v.CheckInAtUtc != null || v.CheckOutAtUtc != null))
            .Select(v => new { v.EmployeeId, v.CheckInAtUtc, v.CheckOutAtUtc })
            .ToListAsync(ct);

        var empIds = records.Select(r => r.EmployeeId)
            .Concat(field.Select(v => v.EmployeeId)).Distinct().ToList();
        var names = await _db.Employees.Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        // (time, name, isIn, isField)
        var events = new List<(DateTime At, string Name, bool In, bool Field)>();
        foreach (var r in records)
        {
            var name = names.GetValueOrDefault(r.EmployeeId, "İşçi");
            if (r.CheckInAtUtc is DateTime ci) events.Add((ci, name, true, false));
            if (r.CheckOutAtUtc is DateTime co) events.Add((co, name, false, false));
        }
        foreach (var v in field)
        {
            var name = names.GetValueOrDefault(v.EmployeeId, "İşçi");
            if (v.CheckInAtUtc is DateTime ci) events.Add((ci, name, true, true));
            if (v.CheckOutAtUtc is DateTime co) events.Add((co, name, false, true));
        }
        var activity = events
            .OrderByDescending(e => e.At)
            .Take(14)
            .Select(e => new
            {
                message = e.Field
                    ? $"{e.Name} — sahəyə {(e.In ? "giriş" : "çıxış")} etdi"
                    : $"{e.Name} — {(e.In ? "giriş etdi" : "çıxış etdi")}",
                at = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(e.At, _timeZone)).ToString("HH:mm"),
                isIn = e.In,
                linkTo = e.Field ? "/admin/field-visits" : "/admin/today",
            })
            .ToList();

        return Ok(new
        {
            // Badge = number of notification rows needing attention (device changes are per-item; the
            // rest are one summary row each) + birthdays.
            totalCount = items.Count,
            items,
            activity,
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
