using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// What the support assistant is allowed to know: a handful of read-only lookups, every one scoped to
/// the CALLING employee. This class is the assistant's entire reach into the database — the LLM never
/// sees a query, only these results — so the security review of "what can the chat leak?" is a review
/// of this file. No method takes a target employee id: the caller IS the subject, always.
/// </summary>
public sealed class AssistantDataService
{
    /// <summary>Server codes → what they mean to the person, in their language. The model gets both:
    /// the code to reason about, the label to say.</summary>
    private static readonly Dictionary<string, string> ReasonAz = new()
    {
        ["DeviceMismatch"] = "skan qeydiyyatda olmayan telefondan/brauzerdən edilib",
        ["NoDeviceBound"] = "hesaba hələ heç bir cihaz bağlanmayıb",
        ["OutsideRadius"] = "skan iş yerinin ərazisindən kənarda edilib",
        ["TokenExpired"] = "QR kod köhnəlib — plakat yenilənib",
        ["LocationInactive"] = "lokasiya deaktiv edilib",
        ["LocationNotFound"] = "lokasiya tapılmadı",
        ["TooSoonToCheckOut"] = "girişdən çox az vaxt keçib (çıxış üçün tezdir)",
        ["AlreadyCompleted"] = "həmin günün giriş-çıxışı artıq tamamlanıb",
        ["EmployeeNotFoundOrInactive"] = "hesab deaktivdir",
        ["OfflineTooOld"] = "oflayn skan çox gec göndərilib",
        ["GpsPermissionDenied"] = "telefonda məkan icazəsi verilməyib",
        ["GpsUnavailable"] = "telefonda məkan xidməti bağlıdır",
    };

    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    // TimeZoneInfo injected raw rather than via AppOptions: that type lives in Application, which
    // this layer does not reference (Application references Infrastructure, not the reverse).
    public AssistantDataService(AppDbContext db, TimeZoneInfo timeZone)
    {
        _db = db;
        _timeZone = timeZone;
    }

    private DateTime NowLocal => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
    private string Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone).ToString("dd.MM.yyyy HH:mm");

    /// <summary>Today's attendance as the boards see it: the record keyed by the server UTC day.</summary>
    public async Task<object> TodayStatusAsync(Guid employeeId, CancellationToken ct)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var record = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate == todayUtc)
            .Select(r => new { r.CheckInAtUtc, r.CheckOutAtUtc, Status = r.Status.ToString() })
            .FirstOrDefaultAsync(ct);

        if (record is null)
            return new { bugun = "hələ giriş yoxdur", qeyd = "işçi bu gün hələ QR skan etməyib" };
        return new
        {
            giris = record.CheckInAtUtc is DateTime i ? Local(i) : null,
            cixis = record.CheckOutAtUtc is DateTime o ? Local(o) : null,
            status = record.Status,
            qeyd = record.CheckOutAtUtc is null ? "gün hələ açıqdır — çıxış skanı edilməyib" : "gün tamamlanıb",
        };
    }

    /// <summary>The employee's most recent rejected scan within a week — the single most useful fact
    /// for "skan alınmır", because it says WHY in the server's own words.</summary>
    public async Task<object> LastRejectedScanAsync(Guid employeeId, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var rejected = await _db.AuditLogs
            .Where(a => a.EmployeeId == employeeId
                        && a.CreatedAtUtc >= since
                        && (a.EventType == AuditEventType.CheckInRejected
                            || a.EventType == AuditEventType.CheckOutRejected
                            || a.EventType == AuditEventType.ScanBlockedOnDevice))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { a.Reason, a.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);

        if (rejected is null)
            return new { son7gun = "rədd edilmiş skan yoxdur" };

        // The scan-failure path stores "Code|detail" — the code is the part that means something.
        var code = (rejected.Reason ?? string.Empty).Split('|')[0];
        return new
        {
            vaxt = Local(rejected.CreatedAtUtc),
            kod = code,
            izah = ReasonAz.GetValueOrDefault(code, "naməlum səbəb"),
        };
    }

    /// <summary>Active device bindings + whether a device-change request is already waiting, so the
    /// assistant never tells someone to file a request they have already filed.</summary>
    public async Task<object> DeviceStatusAsync(Guid employeeId, CancellationToken ct)
    {
        var devices = await _db.DeviceBindings
            .Where(d => d.EmployeeId == employeeId && d.IsActive)
            .OrderByDescending(d => d.LastSeenAtUtc)
            .Select(d => new { d.DeviceLabel, d.LastSeenAtUtc })
            .ToListAsync(ct);

        var pending = await _db.DeviceChangeRequests
            .AnyAsync(r => r.EmployeeId == employeeId && r.Status == DeviceChangeStatus.Pending, ct);

        return new
        {
            aktivCihazSayi = devices.Count,
            cihazlar = devices.Select(d => new
            {
                ad = d.DeviceLabel,
                sonIstifade = d.LastSeenAtUtc is DateTime u ? Local(u) : null,
            }),
            gozleyenTelefonTelebi = pending,
            qeyd = pending
                ? "yeni telefon tələbi artıq göndərilib və admin təsdiqini gözləyir — təzəsini göndərməyə ehtiyac yoxdur"
                : "yeni telefon tələbi yoxdur",
        };
    }

    /// <summary>Days in the last month with a check-in and no check-out. Each one is a day that pays
    /// ZERO hours until an admin closes it — the single costliest thing an employee can not know.</summary>
    public async Task<object> OpenDaysAsync(Guid employeeId, CancellationToken ct)
    {
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = todayUtc.AddDays(-30);
        var days = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId
                        && r.AttendanceDate >= from && r.AttendanceDate < todayUtc
                        && r.CheckInAtUtc != null && r.CheckOutAtUtc == null)
            .OrderByDescending(r => r.AttendanceDate)
            .Select(r => r.AttendanceDate)
            .ToListAsync(ct);

        return new
        {
            acikGunSayi = days.Count,
            gunler = days.Select(d => d.ToString("dd.MM.yyyy")),
            qeyd = days.Count > 0
                ? "bu günlər çıxış skanı edilmədiyi üçün 0 saat sayılır; düzəldilməsi üçün işçi rəhbərinə deməlidir — admin günü bağlaya bilir"
                : "son 30 gündə açıq qalmış gün yoxdur",
        };
    }

    /// <summary>This month so far, from the same table the tabel reads — never today (today is live).</summary>
    public async Task<object> MonthSummaryAsync(Guid employeeId, CancellationToken ct)
    {
        var nowLocal = NowLocal;
        var first = new DateOnly(nowLocal.Year, nowLocal.Month, 1);
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

        var rows = await _db.DailySummaries
            .Where(s => s.EmployeeId == employeeId && s.SummaryDate >= first && s.SummaryDate < todayUtc)
            .Select(s => new { s.WorkedMinutes, s.Status, s.LateMinutes })
            .ToListAsync(ct);

        return new
        {
            ay = nowLocal.ToString("MMMM yyyy"),
            islenmisGun = rows.Count(r => r.WorkedMinutes > 0),
            cemiSaat = Math.Round(rows.Sum(r => r.WorkedMinutes) / 60.0, 1),
            gecikmeGunu = rows.Count(r => r.Status == DailySummaryStatus.Late),
            qayibGunu = rows.Count(r => r.Status == DailySummaryStatus.Absent),
            qeyd = "bugünkü gün daxil deyil — gün bitməmiş hesablanmır; maaş sualları üçün rəhbərə müraciət olunmalıdır",
        };
    }
}
