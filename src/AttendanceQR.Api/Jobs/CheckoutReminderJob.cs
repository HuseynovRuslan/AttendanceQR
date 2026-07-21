using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Pushes "you forgot to check out" to anyone still checked in a while after their shift ended.
/// The one nudge that reaches an employee who has already left the building — every other reminder
/// only works if they happen to open the app.
///
/// Deliberately conservative: one push per open day (CheckoutReminderSentAtUtc), never auto-closing
/// anything, and a silent no-op when push isn't configured. Sweeps every 10 minutes, per tenant,
/// following the same scope/tenant pattern as DailySummaryJob.
/// </summary>
public sealed class CheckoutReminderJob : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PushOptions _push;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<CheckoutReminderJob> _logger;

    public CheckoutReminderJob(
        IServiceScopeFactory scopeFactory, PushOptions push, AppOptions appOptions, ILogger<CheckoutReminderJob> logger)
    {
        _scopeFactory = scopeFactory;
        _push = push;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_push.IsConfigured)
        {
            _logger.LogInformation("CheckoutReminderJob: push not configured — job idle");
            return;
        }

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
                _logger.LogError(ex, "CheckoutReminderJob: sweep failed");
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
                var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();

                var nowUtc = DateTime.UtcNow;
                // Records are keyed by the server UTC day (see the scan handler); look at today and
                // yesterday so an overnight shift that ended after midnight is still covered.
                var today = DateOnly.FromDateTime(nowUtc);
                var from = today.AddDays(-1);

                var open = await db.AttendanceRecords
                    .Where(r => r.AttendanceDate >= from && r.CheckInAtUtc != null && r.CheckOutAtUtc == null
                                && r.CheckoutReminderSentAtUtc == null)
                    .ToListAsync(ct);
                if (open.Count == 0)
                    continue;

                var employeeIds = open.Select(r => r.EmployeeId).Distinct().ToList();
                var employees = await db.Employees
                    .Where(e => employeeIds.Contains(e.Id) && e.IsActive)
                    .ToDictionaryAsync(e => e.Id, ct);
                var locationIds = open.Select(r => r.LocationId).Distinct().ToList();
                var locations = await db.Locations
                    .Where(l => locationIds.Contains(l.Id))
                    .ToDictionaryAsync(l => l.Id, ct);

                var due = new List<Domain.Entities.AttendanceRecord>();
                foreach (var r in open)
                {
                    if (!employees.TryGetValue(r.EmployeeId, out var employee)) continue;
                    if (!locations.TryGetValue(r.LocationId, out var location)) continue;

                    // The employee's own hours when set, else the location's.
                    var shiftEnd = employee.WorkEnd ?? location.ShiftEnd;
                    var shiftStart = employee.WorkStart ?? location.ShiftStart;

                    // When did this shift actually end, in UTC? Start from the local check-in day and
                    // put the end on the next day for an overnight shift (end earlier than start).
                    var checkInLocal = TimeZoneInfo.ConvertTimeFromUtc(r.CheckInAtUtc!.Value, _timeZone);
                    var endLocalDate = DateOnly.FromDateTime(checkInLocal);
                    if (shiftEnd < shiftStart) endLocalDate = endLocalDate.AddDays(1);
                    var endLocal = endLocalDate.ToDateTime(shiftEnd);
                    var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), _timeZone);

                    // Due once the shift ended far enough ago — and give up after 12h so a long-forgotten
                    // day doesn't produce a push the next morning out of nowhere.
                    var since = nowUtc - endUtc;
                    if (since >= TimeSpan.FromMinutes(_push.CheckoutReminderDelayMinutes) && since <= TimeSpan.FromHours(12))
                        due.Add(r);
                }
                if (due.Count == 0)
                    continue;

                var dueEmployeeIds = due.Select(r => r.EmployeeId).Distinct().ToList();
                var subs = await db.PushSubscriptions
                    .Where(s => dueEmployeeIds.Contains(s.EmployeeId))
                    .ToListAsync(ct);
                var subsByEmployee = subs.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

                var dead = new List<Domain.Entities.PushSubscription>();
                foreach (var r in due)
                {
                    if (!subsByEmployee.TryGetValue(r.EmployeeId, out var mine) || mine.Count == 0)
                        continue;   // nothing to send to — leave the record unmarked so a later subscribe still gets it

                    var sentAny = false;
                    foreach (var s in mine)
                    {
                        var alive = await sender.SendAsync(
                            s.Endpoint, s.P256dh, s.Auth,
                            "Çıxış etməyi unutmayın",
                            "İş vaxtınız bitib, amma çıxış qeyd olunmayıb. Çıxışı skan etməsəniz bu gün 0 saat sayılacaq.",
                            "/scan", ct);
                        if (alive) sentAny = true;
                        else dead.Add(s);
                    }
                    if (sentAny) r.CheckoutReminderSentAtUtc = nowUtc;
                }

                if (dead.Count > 0) db.PushSubscriptions.RemoveRange(dead);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "CheckoutReminderJob: tenant {Tenant} — {Due} due, {Dead} dead subscriptions pruned", tenantId, due.Count, dead.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckoutReminderJob: tenant {Tenant} failed", tenantId);
            }
        }
    }
}
