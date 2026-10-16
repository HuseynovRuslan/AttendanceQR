using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
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
///
/// Admin-only today. In a company switched to Enforced it is everybody's who holds «dashboard.view» (Scoped):
/// each count and each line narrows itself to the people the caller holds that line's right over — a
/// timekeeper's bell counts their branch's phones and forgotten PINs, not the company's (see Reach).
/// </summary>
[Permission(AccessPermissions.DashboardView, Scoped = true)]
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

    /// <summary>
    /// What a non-admin's bell may count, in a company switched to Enforced: the people at branches where the
    /// caller holds a line's right (and sees people), and the PIN requests the model lets them answer — the same
    /// rows their own screens show. Null for the Company administrator, and anywhere outside Enforced: the bell
    /// counts the whole company, as it always has.
    /// </summary>
    private sealed class Reach
    {
        private readonly EnforcedRows _rows;
        private readonly Dictionary<Guid, Guid> _homes;

        public Reach(EnforcedRows rows, Dictionary<Guid, Guid> homes)
        {
            _rows = rows;
            _homes = homes;
        }

        public bool Holds(string permission) => _rows.Me.Has(permission);

        public List<Guid> People(string permission) => _homes
            .Where(kv => _rows.Me.Covers(permission, kv.Value) && _rows.Me.Covers(AccessPermissions.EmployeesView, kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        public bool MayAnswerPin(Guid employeeId) =>
            _homes.TryGetValue(employeeId, out var home) && _rows.Allows(ShadowRules.CredentialTarget, employeeId, home);
    }

    private async Task<Reach?> ReachAsync(CancellationToken ct)
    {
        var rows = await EnforcedRows.LoadAsync(_db, User.EmployeeId(), ct);
        if (rows is null || AccessProjection.IsCompanyAdmin(rows.Me))
            return null;
        var homes = await _db.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.LocationId })
            .ToDictionaryAsync(e => e.Id, e => e.LocationId, ct);
        return new Reach(rows, homes);
    }

    private async Task<int> PendingPinResetsAsync(Reach? reach, CancellationToken ct)
    {
        if (reach is null)
            return await _db.PinResetRequests.CountAsync(p => p.Status == PinResetStatus.Pending, ct);
        if (!reach.Holds(AccessPermissions.EmployeesResetPin))
            return 0;
        var pending = await _db.PinResetRequests.Where(p => p.Status == PinResetStatus.Pending)
            .Select(p => p.EmployeeId).ToListAsync(ct);
        return pending.Count(reach.MayAnswerPin);
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
        var reach = await ReachAsync(ct);
        var phones = reach?.People(AccessPermissions.DevicesView);
        var days = reach?.People(AccessPermissions.AttendanceView);

        var pendingDevices = await _deviceChangeService.GetPendingAsync(ct);
        var deviceChanges = phones is null ? pendingDevices.Count : pendingDevices.Count(p => phones.Contains(p.EmployeeId));
        var pinResets = await PendingPinResetsAsync(reach, ct);
        // Today excluded — an open record today is just someone still at work.
        var open = _db.AttendanceRecords
            .Where(r => r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < todayLocal);
        var openRecords = await (days is null ? open : open.Where(r => days.Contains(r.EmployeeId))).CountAsync(ct);

        // Open team-board items past their date. The board is the only place these show today, and a
        // deadline nobody is reminded of is a deadline that quietly passes — which is exactly what the
        // 16 items on it had been doing.
        var overdueTasks = reach is not null && !reach.Holds(AccessPermissions.TasksUse) ? 0
            : await _db.Tasks.CountAsync(t => !t.IsDone && t.DueDate != null && t.DueDate < todayLocal, ct);

        // Today's check-ins whose selfie did not verify. At a branch with no poster that verdict IS the
        // anchor, and a red pill on a row nobody opens holds nothing — the count on the shelf does.
        var flagged = _db.AttendanceRecords.Where(r =>
            r.AttendanceDate == todayLocal
            && (r.FaceMatchStatus == FaceMatchStatus.Mismatch
                || r.FaceMatchStatus == FaceMatchStatus.MultiFace
                || r.FaceMatchStatus == FaceMatchStatus.NoFace));
        var suspiciousToday = await (days is null ? flagged : flagged.Where(r => days.Contains(r.EmployeeId))).CountAsync(ct);

        return Ok(new { deviceChanges, pinResets, openRecords, overdueTasks, suspiciousToday });
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ct = HttpContext.RequestAborted;
        var reach = await ReachAsync(ct);
        var phonesOf = reach?.People(AccessPermissions.DevicesView);
        var daysOf = reach?.People(AccessPermissions.AttendanceView);
        var peopleOf = reach?.People(AccessPermissions.EmployeesView);
        var fieldOf = reach?.People(AccessPermissions.FieldVisitsView);

        // ---- Needs-attention items (drive the badge) ----
        var pending = (await _deviceChangeService.GetPendingAsync(ct))
            .Where(p => phonesOf is null || phonesOf.Contains(p.EmployeeId))
            .ToList();
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
        var pinPending = await PendingPinResetsAsync(reach, ct);
        if (pinPending > 0)
            items.Add(new { type = "PendingPinReset", message = $"{pinPending} PIN sıfırlama tələbi gözləyir", linkTo = "/admin/pin-resets" });

        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        // Today's rejected / blocked scans (the Problems screen). Start-of-day in Baku, as a UTC instant.
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(todayLocal.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), _timeZone);
        var rejectedTypes = new[] { AuditEventType.CheckInRejected, AuditEventType.CheckOutRejected, AuditEventType.ScanBlockedOnDevice };
        var rejected = _db.AuditLogs.Where(a => a.CreatedAtUtc >= todayStartUtc && rejectedTypes.Contains(a.EventType));
        var problemsToday = await (daysOf is null ? rejected
            : rejected.Where(a => a.EmployeeId != null && daysOf.Contains(a.EmployeeId.Value))).CountAsync(ct);
        if (problemsToday > 0)
            items.Add(new { type = "Problems", message = $"{problemsToday} rədd edilmiş skan (bu gün)", linkTo = "/admin/problems" });

        // Past days someone checked in but never out (today is excluded — that's just "still at work").
        var open = _db.AttendanceRecords
            .Where(r => r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < todayLocal);
        var openRecords = await (daysOf is null ? open : open.Where(r => daysOf.Contains(r.EmployeeId))).CountAsync(ct);
        if (openRecords > 0)
            items.Add(new { type = "OpenRecords", message = $"{openRecords} gün çıxış edilməyib", linkTo = "/admin/open-records" });

        var birthdays = await BirthdayItemsAsync(peopleOf);
        items.AddRange(birthdays);

        // ---- Recent activity (informational — today's latest check-ins / check-outs, office AND field).
        // Does NOT inflate the badge (that would run to hundreds); the admin opens the bell to glance. ----
        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == todayLocal && (r.CheckInAtUtc != null || r.CheckOutAtUtc != null))
            .Where(r => daysOf == null || daysOf.Contains(r.EmployeeId))
            .Select(r => new { r.EmployeeId, r.CheckInAtUtc, r.CheckOutAtUtc })
            .ToListAsync(ct);
        var field = await _db.FieldVisits
            .Where(v => v.VisitDate == todayLocal && (v.CheckInAtUtc != null || v.CheckOutAtUtc != null))
            .Where(v => fieldOf == null || fieldOf.Contains(v.EmployeeId))
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
    private async Task<List<object>> BirthdayItemsAsync(List<Guid>? people)
    {
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        var tomorrow = todayLocal.AddDays(1);

        var withDob = await _db.Employees
            .Where(e => e.IsActive && e.BirthDate != null)
            .Where(e => people == null || people.Contains(e.Id))
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
