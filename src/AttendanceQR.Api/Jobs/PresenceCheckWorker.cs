using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Runs presence checks («Mövcudluq yoxlaması», see <see cref="PresenceCheck"/>): once a minute, per company,
/// <see cref="PresenceCheckSweep.RunAsync"/>. Everything it decides is in the database, so a restart picks up where
/// the last process stopped.
/// </summary>
public sealed class PresenceCheckWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly PushOptions _push;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<PresenceCheckWorker> _logger;

    public PresenceCheckWorker(IServiceScopeFactory scopes, PushOptions push, AppOptions options, ILogger<PresenceCheckWorker> logger)
    {
        _scopes = scopes;
        _push = push;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                List<Guid> tenants;
                using (var scope = _scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    tenants = await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
                }

                foreach (var tenantId in tenants)
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var notifier = scope.ServiceProvider.GetRequiredService<IPushNotifier>();
                        var outcome = await PresenceCheckSweep.RunAsync(
                            db, notifier, _push.IsConfigured, DateTime.UtcNow, _timeZone, Random.Shared, ct);
                        if (outcome.Any)
                            _logger.LogInformation("Presence checks for {Tenant}: {Outcome}", tenantId, outcome);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        // One company failing must not stop the others.
                        _logger.LogError(ex, "Presence checks for {Tenant} failed", tenantId);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Presence check sweep failed");
            }

            try
            {
                await Task.Delay(Tick, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

public sealed record PresenceSweepOutcome(int Planned, int Sent, int Cancelled, int NoAnswer)
{
    public bool Any => Planned + Sent + Cancelled + NoAnswer > 0;
}

/// <summary>
/// One pass over one company (the caller has resolved the tenant): plan the day's checks for anybody it is on
/// for who is checked in, send the ones that are due, and close the ones whose answer window has run out.
///
/// Reads only the people presence checks are on for — usually nobody — so a pass a minute costs one indexed
/// query per company on an ordinary day.
/// </summary>
public static class PresenceCheckSweep
{
    public static async Task<PresenceSweepOutcome> RunAsync(
        AppDbContext db, IPushNotifier notifier, bool pushConfigured, DateTime nowUtc, TimeZoneInfo timeZone,
        Random random, CancellationToken ct = default)
    {
        var planned = await PlanAsync(db, nowUtc, timeZone, random, ct);
        var (sent, cancelled) = await SendDueAsync(db, notifier, pushConfigured, nowUtc, ct);
        var (noAnswer, closedFirst) = await CloseExpiredAsync(db, nowUtc, ct);
        return new PresenceSweepOutcome(planned, sent, cancelled + closedFirst, noAnswer);
    }

    // --- 1. plan ------------------------------------------------------------------------------------------

    private static async Task<int> PlanAsync(AppDbContext db, DateTime nowUtc, TimeZoneInfo timeZone, Random random, CancellationToken ct)
    {
        var people = await db.Employees
            .Where(e => e.IsActive && e.PresenceCheckUntilUtc != null && e.PresenceCheckUntilUtc > nowUtc)
            .ToListAsync(ct);
        if (people.Count == 0)
            return 0;

        var ids = people.Select(e => e.Id).ToList();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone));
        var since = today.AddDays(-1);   // a night shift's day began yesterday

        var open = await db.AttendanceRecords
            .Where(r => ids.Contains(r.EmployeeId) && r.AttendanceDate >= since
                        && r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.VoidedAtUtc == null)
            .ToListAsync(ct);
        if (open.Count == 0)
            return 0;

        var recordIds = open.Select(r => r.Id).ToList();
        var alreadyPlanned = (await db.PresenceChecks
                .Where(c => recordIds.Contains(c.AttendanceRecordId))
                .Select(c => c.AttendanceRecordId)
                .ToListAsync(ct))
            .ToHashSet();
        var perDay = (await db.PresenceChecks
                .Where(c => ids.Contains(c.EmployeeId) && c.CheckDate >= since)
                .Select(c => new { c.EmployeeId, c.CheckDate })
                .ToListAsync(ct))
            .GroupBy(c => (c.EmployeeId, c.CheckDate))
            .ToDictionary(g => g.Key, g => g.Count());

        var byId = people.ToDictionary(e => e.Id);
        var locations = await db.Locations.ToDictionaryAsync(l => l.Id, ct);
        var schedules = await db.Schedules.ToDictionaryAsync(s => s.Id, ct);
        var overrides = await ShiftOverrideMap.LoadAsync(db, since, since.AddDays(31), ids, ct);

        var added = 0;
        foreach (var record in open.Where(r => !alreadyPlanned.Contains(r.Id)))
        {
            var employee = byId[record.EmployeeId];
            if (!locations.TryGetValue(employee.LocationId, out var home))
                continue;

            var left = PresenceCheckRules.MaxPerDay - perDay.GetValueOrDefault((employee.Id, record.AttendanceDate));
            if (left <= 0)
                continue;

            // The shift the scan and the reminders judge this day by — through the day's cover shift.
            var shift = EffectiveShift.Resolve(
                employee, overrides.ScheduleFor(employee.Id, record.AttendanceDate, employee.ScheduleId, schedules), home);
            var shiftEnd = ShiftEndAfter(shift, record.AttendanceDate, record.CheckInAtUtc!.Value, timeZone);
            if (PresenceCheckRules.Window(record.CheckInAtUtc.Value, nowUtc, shiftEnd) is not { } window)
                continue;

            var times = PresenceCheckRules.PlanTimes(window.Start, window.End, left, random);
            for (var slot = 0; slot < times.Count; slot++)
            {
                db.PresenceChecks.Add(new PresenceCheck
                {
                    EmployeeId = employee.Id,
                    AttendanceRecordId = record.Id,
                    LocationId = record.LocationId,
                    CheckDate = record.AttendanceDate,
                    Slot = slot,
                    DueAtUtc = times[slot],
                });
                added++;
            }
        }

        if (added == 0)
            return 0;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another pass planned the same day first (the unique (record, slot) index). Its plan stands.
            foreach (var entry in db.ChangeTracker.Entries<PresenceCheck>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            return 0;
        }
        return added;
    }

    /// <summary>
    /// When the shift this check-in belongs to ends: the first of the day's ends (the ordinary one, then a split
    /// day's second window) that falls after the check-in, a night shift's on the next morning. Null when the
    /// person checked in after all of them — they are working outside their hours, and the window falls back.
    /// </summary>
    public static DateTime? ShiftEndAfter(EffectiveShift shift, DateOnly day, DateTime checkInUtc, TimeZoneInfo timeZone)
    {
        var (start, end) = shift.HoursOn(day);
        var candidates = new List<DateTime> { ToUtc(end < start ? day.AddDays(1) : day, end, timeZone) };
        var last = shift.LastEndOn(day);
        if (last != end)
        {
            var second = ToUtc(day, last, timeZone);
            candidates.Add(second <= checkInUtc ? second.AddDays(1) : second);
        }
        return candidates.Where(c => c > checkInUtc).OrderBy(c => c).Cast<DateTime?>().FirstOrDefault();
    }

    private static DateTime ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(day.ToDateTime(time), DateTimeKind.Unspecified), timeZone);

    // --- 2. send ------------------------------------------------------------------------------------------

    private static async Task<(int Sent, int Cancelled)> SendDueAsync(
        AppDbContext db, IPushNotifier notifier, bool pushConfigured, DateTime nowUtc, CancellationToken ct)
    {
        var due = await db.PresenceChecks
            .Where(c => c.Result == PresenceCheckResult.Pending && c.SentAtUtc == null && c.DueAtUtc <= nowUtc)
            .ToListAsync(ct);
        if (due.Count == 0)
            return (0, 0);

        var people = due.Select(c => c.EmployeeId).Distinct().ToList();
        var records = due.Select(c => c.AttendanceRecordId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => people.Contains(e.Id))
            .Select(e => new { e.Id, e.IsActive, e.PresenceCheckUntilUtc })
            .ToDictionaryAsync(e => e.Id, ct);
        var days = await db.AttendanceRecords.AsNoTracking()
            .Where(r => records.Contains(r.Id))
            .Select(r => new { r.Id, r.CheckOutAtUtc, r.VoidedAtUtc })
            .ToDictionaryAsync(r => r.Id, ct);
        // Out on a field visit: away from the branch with leave to be, so being asked «where are you» is wrong.
        var onFieldVisit = (await db.FieldVisits
                .Where(v => people.Contains(v.EmployeeId) && v.Status == FieldVisitStatus.CheckedIn)
                .Select(v => v.EmployeeId)
                .ToListAsync(ct))
            .ToHashSet();

        var toSend = new List<PresenceCheck>();
        var cancelled = 0;
        foreach (var check in due)
        {
            // Re-read, not trusted from the plan: everything may have changed since it was drawn.
            var person = employees.GetValueOrDefault(check.EmployeeId);
            var day = days.GetValueOrDefault(check.AttendanceRecordId);
            var reason =
                person is null || !person.IsActive ? PresenceCheckRules.NoteInactive
                : !PresenceCheckRules.IsOn(person.PresenceCheckUntilUtc, nowUtc) ? PresenceCheckRules.NoteDisabled
                : day is null || day.CheckOutAtUtc != null || day.VoidedAtUtc != null ? PresenceCheckRules.NoteDayClosed
                : nowUtc - check.DueAtUtc > PresenceCheckRules.SendTolerance ? PresenceCheckRules.NoteStale
                : onFieldVisit.Contains(check.EmployeeId) ? PresenceCheckRules.NoteFieldVisit
                : null;
            if (reason is not null)
            {
                check.Result = PresenceCheckResult.Cancelled;
                check.Note = reason;
                cancelled++;
                continue;
            }
            check.SentAtUtc = nowUtc;
            toSend.Add(check);
        }

        // Marked sent BEFORE the push: a pass that dies between the two loses one push, never sends one twice.
        await db.SaveChangesAsync(ct);

        foreach (var check in toSend)
        {
            // The home-page card asks them even when this reaches no phone (an iPhone without the app on the home
            // screen gets no push at all), so a failure here is not a failure of the check.
            var reached = 0;
            if (pushConfigured)
            {
                try
                {
                    reached = await notifier.NotifyEmployeesAsync(
                        [check.EmployeeId], PresenceCheckRules.PushTitle, PresenceCheckRules.PushBody, "/home", ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    reached = 0;
                }
            }
            check.Reached = reached;
        }
        if (toSend.Count > 0)
            await db.SaveChangesAsync(ct);

        return (toSend.Count, cancelled);
    }

    // --- 3. close what ran out ----------------------------------------------------------------------------

    private static async Task<(int NoAnswer, int ClosedFirst)> CloseExpiredAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var cutoff = nowUtc - PresenceCheckRules.AnswerWindow;
        var waiting = await db.PresenceChecks
            .Where(c => c.Result == PresenceCheckResult.Pending && c.SentAtUtc != null && c.SentAtUtc <= cutoff)
            .ToListAsync(ct);
        if (waiting.Count == 0)
            return (0, 0);

        var records = waiting.Select(c => c.AttendanceRecordId).Distinct().ToList();
        var closedAt = await db.AttendanceRecords.AsNoTracking()
            .Where(r => records.Contains(r.Id))
            .Select(r => new { r.Id, r.CheckOutAtUtc })
            .ToDictionaryAsync(r => r.Id, r => r.CheckOutAtUtc, ct);

        int noAnswer = 0, closedFirst = 0;
        foreach (var check in waiting)
        {
            // Checked out before their time to answer was up: they ended the day rather than ignored the question.
            if (closedAt.GetValueOrDefault(check.AttendanceRecordId) is DateTime left
                && left < check.SentAtUtc!.Value + PresenceCheckRules.AnswerWindow)
            {
                check.Result = PresenceCheckResult.Cancelled;
                check.Note = PresenceCheckRules.NoteDayClosed;
                closedFirst++;
            }
            else
            {
                check.Result = PresenceCheckResult.NoAnswer;
                noAnswer++;
            }
        }
        await db.SaveChangesAsync(ct);
        return (noAnswer, closedFirst);
    }
}
