using System.Threading.Channels;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Somebody cannot clock in for a reason the manager at their site can fix, or has asked for a new
/// phone to be approved. Raised by the scan path, delivered by StaffAlertWorker.
///
/// Managers had no way to hear about either. On 03.10.2026 sixteen people at Bakı Abadlıq were refused
/// by their own phones and stayed absent — most for a location permission someone standing next to
/// them could have switched on in a minute — and a device-change request waited more than twenty hours
/// for a manager who was allowed to approve it and never knew it was there.
/// </summary>
public sealed record StaffAlert(Guid TenantId, Guid EmployeeId, string Kind, DateTime RaisedAtUtc);

/// <summary>
/// Hands staff alerts from the request to a background worker. The scan never waits on a push: a
/// check-in is not held up by anything optional, and a notification is optional.
/// </summary>
public interface IStaffAlertQueue
{
    void Enqueue(StaffAlert alert);
    ChannelReader<StaffAlert> Reader { get; }
}

public sealed class StaffAlertQueue : IStaffAlertQueue
{
    // Bounded and lossy on purpose: an alert is a courtesy, and a phone stuck in a retry loop must not
    // be able to grow this without limit. Each person is alerted about each problem once a day anyway.
    private readonly Channel<StaffAlert> _channel = Channel.CreateBounded<StaffAlert>(
        new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Enqueue(StaffAlert alert) => _channel.Writer.TryWrite(alert);
    public ChannelReader<StaffAlert> Reader => _channel.Reader;
}

/// <summary>Which refusals are worth a manager's attention, what the push says, and when it goes. The
/// one place this is decided.</summary>
public static class StaffAlertKinds
{
    public const string DeviceChangeRequested = "DeviceChangeRequested";

    /// <summary>
    /// How long a refused person gets to sort it out alone before their manager hears about it. Most
    /// do: 207 people met a location-permission refusal in the week to 03.10.2026, nearly all for a
    /// minute or two. Alerting on every one would teach managers to swipe these away; waiting, and
    /// staying quiet when the person has scanned since, leaves only the ones who are really stuck.
    /// </summary>
    public static readonly TimeSpan GraceForBlocked = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Nobody is woken for this. On the first morning these went out, one landed at 04:07. An alert that
    /// falls due between QuietFrom and QuietUntil, company time, waits for the morning — and is still
    /// dropped then if the person has scanned since, so a night-shift worker who got in at 05:00 is never
    /// reported at all.
    /// </summary>
    public static readonly TimeOnly QuietFrom = new(22, 0);
    public static readonly TimeOnly QuietUntil = new(6, 30);

    // Only what the manager can fix on the spot. Not a weak GPS signal or a busy camera — those clear
    // on their own — and not «outside the radius», which is mostly somebody scanning from the bus.
    private static readonly Dictionary<string, (string Problem, string Fix, string Url)> Blocked = new()
    {
        ["GpsPermissionDenied"] = ("telefonunda məkan (GPS) icazəsi bağlıdır", "İcazəni açmağa kömək edin.", "/admin/problems"),
        ["GpsUnavailable"] = ("telefonunda məkan xidməti (GPS) söndürülüb", "Yandırmağa kömək edin.", "/admin/problems"),
        ["CameraDenied"] = ("telefonunda kamera icazəsi bağlıdır", "İcazəni açmağa kömək edin.", "/admin/problems"),
        ["CameraNotFound"] = ("telefonunun kamerası açılmır", "Bu gün gəlişini əl ilə qeyd edin.", "/admin/problems"),
        ["DeviceBindLimit"] = ("telefonu tətbiqi yadda saxlamır", "Yeni telefon tələbini təsdiqləyin və tətbiqi ana ekrandakı ikondan açmağı göstərin.", "/admin/device-changes"),
        ["DeviceRevoked"] = ("hesabından çıxarılmış telefonla skan edir", "Telefon onunkudursa, yeni telefon tələbini təsdiqləyin.", "/admin/device-changes"),
        ["DeviceMismatch"] = ("telefonu hesabına bağlı deyil", "Yeni telefon tələbi göndərsin, siz təsdiqləyin.", "/admin/device-changes"),
        ["NoDeviceBound"] = ("hesabına heç bir telefon bağlı deyil", "Yeni telefon tələbi göndərsin, siz təsdiqləyin.", "/admin/device-changes"),
        ["SharedDeviceNotAllowed"] = ("başqasının telefonu ilə skan edir", "Ona ortaq telefon icazəsi verin.", "/admin/device-changes"),
        ["DeviceAccountLimit"] = ("skan etdiyi telefonda həddən çox hesab var", "Hesablardan birini başqa telefona keçirin.", "/admin/device-changes"),
    };

    public static bool IsAlertable(string kind) => kind == DeviceChangeRequested || Blocked.ContainsKey(kind);

    /// <summary>A refusal waits out the grace period and is dropped if the person scanned meanwhile; a
    /// request goes at once.</summary>
    public static bool WaitsForRecovery(string kind) => Blocked.ContainsKey(kind);

    public static DateTime DueAtUtc(StaffAlert alert, TimeZoneInfo timeZone) => AfterQuietHours(
        WaitsForRecovery(alert.Kind) ? alert.RaisedAtUtc + GraceForBlocked : alert.RaisedAtUtc, timeZone);

    /// <summary>The moment itself when it is outside quiet hours; otherwise QuietUntil on the morning that
    /// ends them.</summary>
    public static DateTime AfterQuietHours(DateTime utc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        var time = TimeOnly.FromDateTime(local);
        if (time >= QuietUntil && time < QuietFrom)
            return utc;
        var morning = local.Date + QuietUntil.ToTimeSpan();
        if (time >= QuietFrom)
            morning = morning.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(morning, DateTimeKind.Unspecified), timeZone);
    }

    /// <summary>The push — title, body, where tapping it leads — or null for a kind nobody hears about.
    /// <paramref name="waited"/> is how long ago the refusal was: ten minutes in the day, a night's worth
    /// for one held until the morning, which must not claim ten.</summary>
    public static (string Title, string Body, string Url)? Describe(string kind, string employeeName, TimeSpan waited)
    {
        if (kind == DeviceChangeRequested)
            return ($"{employeeName} yeni telefon tələbi göndərib", "Təsdiqləsəniz, dərhal skan edə biləcək.", "/admin/device-changes");
        if (!Blocked.TryGetValue(kind, out var b))
            return null;
        var since = waited < TimeSpan.FromHours(1)
            ? $"{Math.Max(GraceForBlocked.TotalMinutes, Math.Floor(waited.TotalMinutes)):0} dəqiqədir"
            : $"{Math.Floor(waited.TotalHours):0} saatdır";
        return ($"{employeeName} skan edə bilmir", $"{since} alınmır: {b.Problem}. {b.Fix}", b.Url);
    }
}

public enum StaffAlertOutcome { Sent, Recovered, AlreadyHandled, AlreadyAlerted, NotAlertable, NoRecipients, UnknownEmployee }

/// <summary>
/// Delivers one due alert: to the managers of the person's own branch — the people standing there —
/// or, where a branch has none, to the company's admins. Once per person, problem and day, recorded as
/// <see cref="AuditEventType.StaffAlertSent"/> so the next refusal knows it has been said.
/// </summary>
public static class StaffAlertDispatcher
{
    public static async Task<StaffAlertOutcome> HandleAsync(
        AppDbContext db, IPushNotifier notifier, StaffAlert alert, DateTime nowUtc, TimeZoneInfo timeZone,
        CancellationToken ct = default)
    {
        if (!StaffAlertKinds.IsAlertable(alert.Kind))
            return StaffAlertOutcome.NotAlertable;

        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == alert.EmployeeId)
            .Select(e => new { e.FullName, e.LocationId })
            .FirstOrDefaultAsync(ct);
        if (employee is null)
            return StaffAlertOutcome.UnknownEmployee;

        if (StaffAlertKinds.WaitsForRecovery(alert.Kind))
        {
            var recovered = await db.AuditLogs.AnyAsync(a =>
                a.EmployeeId == alert.EmployeeId
                && (a.EventType == AuditEventType.CheckInSuccess || a.EventType == AuditEventType.CheckOutSuccess)
                && a.CreatedAtUtc >= alert.RaisedAtUtc, ct);
            if (recovered)
                return StaffAlertOutcome.Recovered;
        }
        else
        {
            // A request held overnight may have been approved or rejected by morning — by an admin who was
            // awake. Telling the manager to approve it then sends them to an empty screen.
            var stillPending = await db.DeviceChangeRequests.AnyAsync(r =>
                r.EmployeeId == alert.EmployeeId && r.Status == DeviceChangeStatus.Pending, ct);
            if (!stillPending)
                return StaffAlertOutcome.AlreadyHandled;
        }

        // Once a day per person and problem, counted on the company's calendar.
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, timeZone);
        var said = alert.Kind + "|";
        var already = await db.AuditLogs.AnyAsync(a =>
            a.EmployeeId == alert.EmployeeId
            && a.EventType == AuditEventType.StaffAlertSent
            && a.CreatedAtUtc >= dayStartUtc
            && a.Reason != null && a.Reason.StartsWith(said), ct);
        if (already)
            return StaffAlertOutcome.AlreadyAlerted;

        var managers = await (
            from ml in db.ManagedLocations
            join m in db.Employees on ml.EmployeeId equals m.Id
            where ml.LocationId == employee.LocationId && m.IsActive && m.Role == EmployeeRole.Manager
                  && m.Id != alert.EmployeeId
            select m.Id).Distinct().ToListAsync(ct);
        var recipients = managers.Count > 0
            ? managers
            : await db.Employees
                .Where(e => e.IsActive && e.Role == EmployeeRole.Admin && e.Id != alert.EmployeeId)
                .Select(e => e.Id).ToListAsync(ct);
        if (recipients.Count == 0)
            return StaffAlertOutcome.NoRecipients;

        var text = StaffAlertKinds.Describe(alert.Kind, employee.FullName, nowUtc - alert.RaisedAtUtc)!.Value;
        var reached = await notifier.NotifyEmployeesAsync(recipients, text.Title, text.Body, text.Url, ct);

        // Written even when nobody had a live subscription: the next refusal must not try again, and
        // «0/2 menecer» is itself worth seeing — those two need notifications switched on.
        db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = alert.EmployeeId,
            EventType = AuditEventType.StaffAlertSent,
            Reason = $"{alert.Kind}|{reached}/{recipients.Count} {(managers.Count > 0 ? "menecer" : "admin")}",
        });
        await db.SaveChangesAsync(ct);
        return StaffAlertOutcome.Sent;
    }
}
