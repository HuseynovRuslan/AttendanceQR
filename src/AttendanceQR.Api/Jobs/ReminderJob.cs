using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// The three attendance reminders, swept every 5 minutes per tenant:
///
///   • CheckInSoon    — the shift starts shortly and there is still no check-in.
///   • CheckOutSoon   — the shift ends shortly and they are still checked in. Sent BEFORE the end on
///                      purpose: after it, the employee is already home and can no longer scan out.
///   • MissedCheckOut — the shift is well over and no check-out was ever recorded; that day counts as
///                      zero hours until an admin closes it.
///
/// Each send is recorded as an <see cref="EmployeeNotification"/>, which does double duty: its unique
/// (employee, type, day) index is the dedupe — a 5-minute sweep can't nag twice — and the rows are the
/// employee's in-app notification list, since a push banner is gone the moment it's swiped away.
///
/// Nothing is ever auto-closed, and no reason is ever asked. Days off (WorkDaysMask / NonWorkingDay)
/// are skipped so nobody is told to come to work on their rest day.
/// </summary>
public sealed class ReminderJob : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
    // How long after the shift ends before "you never checked out" goes out, and how long we keep
    // trying — past that the day is stale and the admin closes it from /admin/open-records.
    private static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan MissedGiveUpAfter = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PushOptions _push;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<ReminderJob> _logger;

    public ReminderJob(
        IServiceScopeFactory scopeFactory, PushOptions push, AppOptions appOptions, ILogger<ReminderJob> logger)
    {
        _scopeFactory = scopeFactory;
        _push = push;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReminderJob: sweep failed");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<List<Guid>> ActiveTenantIdsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        foreach (var tenantId in await ActiveTenantIdsAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<IPushNotifier>();

                var nowUtc = DateTime.UtcNow;
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);
                var todayLocal = DateOnly.FromDateTime(nowLocal);
                // AttendanceRecords are keyed by the server UTC day (see the scan handler).
                var todayUtc = DateOnly.FromDateTime(nowUtc);

                var employees = await db.Employees.Where(e => e.IsActive).ToListAsync(ct);
                if (employees.Count == 0) continue;

                var locations = await db.Locations.ToDictionaryAsync(l => l.Id, ct);
                var records = await db.AttendanceRecords
                    .Where(r => r.AttendanceDate >= todayUtc.AddDays(-1))
                    .ToListAsync(ct);
                var byEmployee = records
                    .GroupBy(r => r.EmployeeId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.AttendanceDate).ToList());

                // Rest days: a company/branch holiday shouldn't produce a "come to work" nudge.
                var holidays = await db.NonWorkingDays
                    .Where(d => d.Date == todayLocal)
                    .ToListAsync(ct);

                foreach (var employee in employees)
                {
                    if (!locations.TryGetValue(employee.LocationId, out var location)) continue;

                    var shiftStart = employee.WorkStart ?? location.ShiftStart;
                    var shiftEnd = employee.WorkEnd ?? location.ShiftEnd;
                    var mine = byEmployee.GetValueOrDefault(employee.Id) ?? new List<AttendanceRecord>();
                    var todayRecord = mine.FirstOrDefault(r => r.AttendanceDate == todayUtc);

                    var offToday =
                        holidays.Any(h => h.LocationId == null || h.LocationId == employee.LocationId) ||
                        (location.WorkDaysMask & (1 << (int)todayLocal.DayOfWeek)) == 0;

                    // 1) Shift starts soon and nobody has checked in.
                    if (!offToday && todayRecord?.CheckInAtUtc is null)
                    {
                        var startUtc = ToUtc(todayLocal, shiftStart);
                        var until = startUtc - nowUtc;
                        if (until > TimeSpan.Zero && until <= TimeSpan.FromMinutes(_push.CheckInReminderLeadMinutes))
                        {
                            await SendAsync(db, notifier, employee.Id, EmployeeNotificationType.CheckInSoon, todayLocal,
                                "İş vaxtınız başlayır",
                                $"{_push.CheckInReminderLeadMinutes} dəqiqəyə növbəniz başlayır. Gələndə QR kodu skan edib giriş etməyi unutmayın.",
                                ct);
                        }
                    }

                    // 2 & 3) An open day — the shift is running or already over.
                    var open = mine.FirstOrDefault(r => r.CheckInAtUtc != null && r.CheckOutAtUtc == null);
                    if (open?.CheckInAtUtc is not DateTime checkInUtc) continue;

                    var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(checkInUtc, _timeZone);
                    var endDate = DateOnly.FromDateTime(checkInLocal);
                    if (shiftEnd < shiftStart) endDate = endDate.AddDays(1);   // overnight shift
                    var endUtc = ToUtc(endDate, shiftEnd);
                    var openDay = DateOnly.FromDateTime(checkInLocal);

                    var untilEnd = endUtc - nowUtc;
                    if (untilEnd > TimeSpan.Zero && untilEnd <= TimeSpan.FromMinutes(_push.CheckoutReminderLeadMinutes))
                    {
                        await SendAsync(db, notifier, employee.Id, EmployeeNotificationType.CheckOutSoon, openDay,
                            "İş vaxtınız bitir",
                            $"{_push.CheckoutReminderLeadMinutes} dəqiqəyə növbəniz bitir. Çıxışı skan etməyi unutmayın — yoxsa bu gün 0 saat sayılacaq.",
                            ct);
                    }
                    else
                    {
                        var since = nowUtc - endUtc;
                        if (since >= MissedAfter && since <= MissedGiveUpAfter)
                        {
                            await SendAsync(db, notifier, employee.Id, EmployeeNotificationType.MissedCheckOut, openDay,
                                "Çıxış qeyd olunmayıb",
                                "İş vaxtınız bitdi, amma çıxış etmədiniz. Bu gün 0 saat sayılır — rəhbərlə əlaqə saxlayın.",
                                ct);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReminderJob: tenant {Tenant} failed", tenantId);
            }
        }
    }

    private DateTime ToUtc(DateOnly localDate, TimeOnly localTime)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified), _timeZone);

    /// <summary>Writes the notification row first — the unique index is the dedupe — and only pushes if
    /// the insert won, so a concurrent sweep can never send the same reminder twice.</summary>
    private async Task SendAsync(
        AppDbContext db, IPushNotifier notifier, Guid employeeId, EmployeeNotificationType type,
        DateOnly relatedDate, string title, string body, CancellationToken ct)
    {
        var row = new EmployeeNotification
        {
            EmployeeId = employeeId,
            Type = type,
            RelatedDate = relatedDate,
            Title = title,
            Body = body,
        };
        var entry = db.EmployeeNotifications.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            entry.State = EntityState.Detached;   // already sent today — nothing to do
            return;
        }

        // Best-effort: the row (and so the in-app list) stands even if the phone can't be reached.
        if (_push.IsConfigured)
            await notifier.NotifyEmployeesAsync(new[] { employeeId }, title, body, "/scan", ct);
    }
}
