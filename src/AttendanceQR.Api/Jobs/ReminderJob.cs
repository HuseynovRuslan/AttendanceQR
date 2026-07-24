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
///   • MissedCheckOut — a day that never closed, reported the MORNING AFTER rather than the same
///                      evening: people routinely scan out well past their nominal end, so an evening
///                      message accused the hardest workers of forgetting.
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

    // "You never checked out" is told the MORNING AFTER, not the same evening.
    //
    // It used to fire 45 minutes past the shift end, which accused precisely the people who work
    // longest: over 14 days, half of all check-outs land within a minute of the nominal end but a
    // tenth are still more than an hour and a half later. On 22 July it went to 27 people and 21 of
    // them had simply not finished yet — Tərlan's shift ended at 18:00, the message went at 18:45,
    // he scanned out at 19:46. By the next morning anyone merely working late has long since scanned
    // out, so what remains is a genuine open day, and saying so is both true and actionable.
    //
    // The shift must also be well over before it counts, which keeps a night shift ending at 07:00
    // from being reported at 09:00 the same morning.
    private static readonly TimeSpan MissedMinAge = TimeSpan.FromHours(8);
    private static readonly TimeSpan MissedGiveUpAfter = TimeSpan.FromHours(48);
    // A civil hour: the message is informational, so it waits for the working day to start.
    private static readonly TimeOnly MissedWindowFrom = new(9, 0);
    private static readonly TimeOnly MissedWindowTo = new(10, 0);

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

                    // 2) The shift ends shortly and they are still checked in — the one nudge that can
                    //    still change the outcome, so it goes out BEFORE the end while they can scan.
                    var untilEnd = endUtc - nowUtc;
                    if (untilEnd > TimeSpan.Zero && untilEnd <= TimeSpan.FromMinutes(_push.CheckoutReminderLeadMinutes))
                    {
                        await SendAsync(db, notifier, employee.Id, EmployeeNotificationType.CheckOutSoon, openDay,
                            "İş vaxtınız bitir",
                            $"{_push.CheckoutReminderLeadMinutes} dəqiqəyə növbəniz bitir. Çıxışı skan etməyi unutmayın — yoxsa bu gün 0 saat sayılacaq.",
                            ct);
                    }

                    // 3) The day never closed. Reported the next morning (see MissedMinAge) — by then
                    //    working late is no longer an explanation, so the message is finally true.
                    var since = nowUtc - endUtc;
                    if (since >= MissedMinAge && since <= MissedGiveUpAfter
                        && nowLocal.TimeOfDay >= MissedWindowFrom.ToTimeSpan()
                        && nowLocal.TimeOfDay < MissedWindowTo.ToTimeSpan())
                    {
                        await SendAsync(db, notifier, employee.Id, EmployeeNotificationType.MissedCheckOut, openDay,
                            "Çıxış qeyd olunmayıb",
                            $"{openDay:dd.MM.yyyy} tarixində çıxışınız qeyd olunmayıb — həmin gün 0 saat sayılır. " +
                            "Düzəldilməsi üçün rəhbərinizlə əlaqə saxlayın.",
                            ct);
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
