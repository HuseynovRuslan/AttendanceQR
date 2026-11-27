using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Notifications;

public sealed record DispatchOutcome(int Events, int Notifications, int Pushes, int Live, int Failed)
{
    public static DispatchOutcome None { get; } = new(0, 0, 0, 0, 0);
    public bool Any => Events + Failed > 0;
}

/// <summary>A live pop-up owed to one person — sent over SignalR after the save (<see cref="INotificationLive"/>).</summary>
public sealed record LiveNotification(
    Guid RecipientId, Guid? Id, string TypeKey, string Title, string Body, string? BranchName,
    DateTime OccurredAtUtc, bool WasOffline, int? Minutes, string? Url);

/// <summary>
/// Turns outbox events into notifications, for one company per call (the caller has resolved the tenant):
///
///   event → who wants this type (settings) → who may see the subject's branch (<see cref="NotificationAudience"/>, read
///   NOW — a revoked grant or a deactivated account receives nothing) → their branch pick → their channels →
///   a center line (<see cref="Notification"/>), an owed push (<see cref="NotificationDelivery"/>), a live pop-up.
///
/// Idempotent: a pass that dies after its save and runs again finds the lines it already wrote (the unique
/// (event, recipient, type) index is the last word) and writes only what is missing. A failing pass puts its events
/// back with a growing delay and gives up after <see cref="MaxAttempts"/>, logging why; the check-ins themselves were
/// committed long before and are never touched. Batch-shaped throughout — one query per kind of row for the whole
/// batch, never one per event — so a shift start with a thousand arrivals is a handful of queries.
/// </summary>
public sealed class NotificationDispatcher
{
    public const int BatchSize = 300;
    public const int MaxAttempts = 8;

    /// <summary>An event older than this when it is processed (an offline scan synced late, an outage) goes to the center
    /// only — no push. A phone ringing at noon about a 09:00 arrival is noise, and a week of offline scans arriving
    /// at once would be a storm.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>How long a pass holds its events before another may take them.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly TimeZoneInfo _timeZone;
    private readonly string[] _hiddenEmails;
    private readonly ILogger _logger;

    public NotificationDispatcher(AppOptions options, ILogger logger)
    {
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _hiddenEmails = options.HiddenEmailList();
        _logger = logger;
    }

    public static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(1),
        3 => TimeSpan.FromMinutes(2),
        4 => TimeSpan.FromMinutes(5),
        5 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30),
    };

    public async Task<(DispatchOutcome Outcome, IReadOnlyList<LiveNotification> Live)> ProcessAsync(
        AppDbContext db, DateTime nowUtc, CancellationToken ct = default)
    {
        var events = await db.NotificationEvents
            .Where(e => e.Status == NotificationEventStatus.Pending && e.NextAttemptUtc <= nowUtc)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (events.Count == 0)
            return (DispatchOutcome.None, []);

        // The lease, written before any work: a second pass (a deploy's overlap) skips these until it runs out.
        foreach (var e in events)
        {
            e.Attempts++;
            e.NextAttemptUtc = nowUtc + Lease;
        }
        await db.SaveChangesAsync(ct);

        try
        {
            var built = await BuildAsync(db, events, nowUtc, ct);

            // Idempotency: what an earlier, interrupted pass already wrote for these events stays as it is.
            var eventIds = events.Select(e => (Guid?)e.Id).ToList();
            var existing = (await db.Notifications.AsNoTracking()
                    .Where(n => eventIds.Contains(n.EventId))
                    .Select(n => new { n.EventId, n.RecipientEmployeeId, n.TypeKey })
                    .ToListAsync(ct))
                .Select(n => (n.EventId!.Value, n.RecipientEmployeeId, n.TypeKey))
                .ToHashSet();

            var written = 0;
            var pushes = 0;
            var live = new List<LiveNotification>();
            foreach (var item in built)
            {
                if (existing.Contains((item.EventId, item.RecipientId, item.TypeKey)))
                    continue;
                if (item.Notification is { } line)
                {
                    db.Notifications.Add(line);
                    written++;
                }
                if (item.Push is { } push)
                {
                    db.NotificationDeliveries.Add(push);
                    pushes++;
                }
                if (item.Live is { } pop)
                    live.Add(pop);
            }

            foreach (var e in events)
            {
                e.Status = NotificationEventStatus.Done;
                e.ProcessedAtUtc = nowUtc;
                e.LastError = null;
            }
            await db.SaveChangesAsync(ct);
            return (new DispatchOutcome(events.Count, written, pushes, live.Count, 0), live);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever the failed pass left half-tracked is dropped; the events go back with a delay.
            db.ChangeTracker.Clear();
            var ids = events.Select(e => e.Id).ToList();
            var again = await db.NotificationEvents.Where(e => ids.Contains(e.Id)).ToListAsync(ct);
            var failed = 0;
            foreach (var e in again)
            {
                e.LastError = SafeText.Truncate($"{ex.GetType().Name}: {ex.Message}", 500);
                if (e.Attempts >= MaxAttempts)
                {
                    e.Status = NotificationEventStatus.Failed;
                    failed++;
                }
                else
                {
                    e.NextAttemptUtc = nowUtc + Backoff(e.Attempts);
                }
            }
            await db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Notifications: {Count} events not dispatched ({Failed} given up); retrying later",
                events.Count, failed);
            return (new DispatchOutcome(0, 0, 0, 0, events.Count), []);
        }
    }

    private sealed record Built(
        Guid EventId, Guid RecipientId, string TypeKey, Notification? Notification, NotificationDelivery? Push, LiveNotification? Live);

    private sealed record Wish(NotificationPreference Preference, IReadOnlySet<string> Types, IReadOnlySet<Guid>? Branches);

    private async Task<List<Built>> BuildAsync(AppDbContext db, List<NotificationEvent> events, DateTime nowUtc, CancellationToken ct)
    {
        var result = new List<Built>();

        // 1. Who wants anything at all. No row, or no type switched on, is «off».
        var preferences = await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.Category == NotificationTypes.CategoryAttendance && p.EnabledTypes != "")
            .ToListAsync(ct);
        if (preferences.Count == 0)
            return result;

        var selectedIds = preferences.Where(p => p.BranchMode == NotificationBranchMode.Selected).Select(p => p.EmployeeId).ToList();
        var picks = selectedIds.Count == 0
            ? new Dictionary<Guid, IReadOnlySet<Guid>>()
            : (await db.NotificationBranchPreferences.AsNoTracking()
                    .Where(b => b.Category == NotificationTypes.CategoryAttendance && selectedIds.Contains(b.EmployeeId))
                    .Select(b => new { b.EmployeeId, b.LocationId })
                    .ToListAsync(ct))
                .GroupBy(b => b.EmployeeId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<Guid>)g.Select(b => b.LocationId).ToHashSet());
        var wishes = preferences
            .Select(p => new Wish(
                p,
                NotificationTypes.Normalize(NotificationTypes.CategoryAttendance, NotificationTypes.ParseCsv(p.EnabledTypes)).ToHashSet(),
                p.BranchMode == NotificationBranchMode.Selected ? picks.GetValueOrDefault(p.EmployeeId) ?? new HashSet<Guid>() : null))
            .Where(w => w.Types.Count > 0)
            .ToList();
        if (wishes.Count == 0)
            return result;

        // 2. Who may see what — now, not when they switched it on.
        var audience = await NotificationAudience.LoadAsync(db, wishes.Select(w => w.Preference.EmployeeId).ToList(), ct);

        // 3. The batch's people, days and branches, each loaded once.
        var subjectIds = events.Select(e => e.SubjectEmployeeId).Distinct().ToList();
        var recordIds = events.Where(e => e.AttendanceRecordId != null).Select(e => e.AttendanceRecordId!.Value).Distinct().ToList();
        var subjects = await db.Employees.AsNoTracking().Where(e => subjectIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var records = await db.AttendanceRecords.AsNoTracking().Where(r => recordIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var locations = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);

        var lateWanted = wishes.Any(w => w.Types.Contains(NotificationTypes.Late));
        var lateness = lateWanted
            ? await LatenessAsync(db, events.Where(e => e.Kind == NotificationEventKinds.CheckIn).ToList(), subjects, records, locations, ct)
            : new Dictionary<Guid, int>();

        foreach (var e in events)
        {
            if (!subjects.TryGetValue(e.SubjectEmployeeId, out var subject) || !IsTrackedStaff(subject))
                continue;
            if (e.AttendanceRecordId is not Guid recordId || !records.TryGetValue(recordId, out var record) || record.VoidedAtUtc != null)
                continue;

            var types = new List<(string Type, int? Minutes)>();
            if (e.Kind == NotificationEventKinds.CheckIn)
            {
                types.Add((NotificationTypes.CheckIn, null));
                if (lateness.TryGetValue(e.Id, out var minutes))
                    types.Add((NotificationTypes.Late, minutes));
            }
            else if (e.Kind == NotificationEventKinds.CheckOut && record.CheckOutAtUtc != null)
            {
                types.Add((NotificationTypes.CheckOut, null));
            }
            if (types.Count == 0)
                continue;

            var home = subject.LocationId;
            var site = e.LocationId is Guid siteId && locations.TryGetValue(siteId, out var at) ? at.Name : null;
            var timeLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc), _timeZone);
            var stale = nowUtc - e.OccurredAtUtc > StaleAfter;
            var url = $"/admin/employees/{subject.Id}";

            foreach (var wish in wishes)
            {
                var recipient = wish.Preference.EmployeeId;
                if (recipient == subject.Id || !audience.Covers(recipient, home))
                    continue;
                if (wish.Branches is { } picked && !picked.Contains(home))
                    continue;

                foreach (var (type, minutes) in types)
                {
                    if (!wish.Types.Contains(type))
                        continue;
                    var (title, body) = Describe(type, subject.FullName, minutes);
                    var pushBody = PushBody(body, site, timeLocal, e.WasOffline);

                    Notification? line = null;
                    if (wish.Preference.InApp)
                    {
                        line = new Notification
                        {
                            RecipientEmployeeId = recipient,
                            EventId = e.Id,
                            TypeKey = type,
                            Title = title,
                            Body = body,
                            SubjectEmployeeId = subject.Id,
                            SubjectName = SafeText.Truncate(subject.FullName, 200),
                            LocationId = home,
                            BranchName = site is null ? null : SafeText.Truncate(site, 200),
                            OccurredAtUtc = e.OccurredAtUtc,
                            WasOffline = e.WasOffline,
                            Minutes = minutes,
                            Url = url,
                            CreatedAtUtc = nowUtc,
                        };
                    }
                    var push = wish.Preference.Push && !stale
                        ? new NotificationDelivery
                        {
                            NotificationId = line?.Id,
                            RecipientEmployeeId = recipient,
                            Channel = NotificationChannel.Push,
                            TypeKey = type,
                            Title = title,
                            Body = pushBody,
                            Url = url,
                            NextAttemptUtc = nowUtc,
                            CreatedAtUtc = nowUtc,
                        }
                        : null;
                    var pop = wish.Preference.Web
                        ? new LiveNotification(recipient, line?.Id, type, title, body, site, e.OccurredAtUtc, e.WasOffline, minutes, url)
                        : null;
                    result.Add(new Built(e.Id, recipient, type, line, push, pop));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Staff whose attendance the board shows — the population <c>ReportPopulation.Members</c> counts: not a
    /// panel-only account (it runs the panel, it is not tracked) and not a hidden system account.
    /// </summary>
    private bool IsTrackedStaff(Employee e) =>
        !e.PanelOnly && (e.Email is null || !_hiddenEmails.Contains(e.Email.Trim().ToLowerInvariant()));

    /// <summary>Minutes late per check-in event, for the events that were late by the person's own shift.</summary>
    private async Task<Dictionary<Guid, int>> LatenessAsync(
        AppDbContext db, List<NotificationEvent> checkIns, IReadOnlyDictionary<Guid, Employee> subjects,
        IReadOnlyDictionary<Guid, AttendanceRecord> records, IReadOnlyDictionary<Guid, Location> locations, CancellationToken ct)
    {
        var result = new Dictionary<Guid, int>();
        var days = checkIns
            .Select(e => e.AttendanceRecordId is Guid id && records.TryGetValue(id, out var r) ? r : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
        if (days.Count == 0)
            return result;

        var people = days.Select(r => r.EmployeeId).Distinct().ToList();
        var dates = days.Select(r => r.AttendanceDate).Distinct().ToList();
        var (from, to) = (dates.Min(), dates.Max());

        var schedules = await db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var plan = await ShiftOverrideMap.LoadAsync(db, from, to, people, ct);
        var holidays = await db.NonWorkingDays.AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to)
            .Select(h => new { h.Date, h.LocationId })
            .ToListAsync(ct);
        var leaves = await db.LeaveRecords.AsNoTracking()
            .Where(l => people.Contains(l.EmployeeId) && l.FromDate <= to && l.ToDate >= from)
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate })
            .ToListAsync(ct);
        var stretches = (await db.AttendanceRecords.AsNoTracking()
                .Where(r => people.Contains(r.EmployeeId) && dates.Contains(r.AttendanceDate) && r.VoidedAtUtc == null)
                .Select(r => new { r.Id, r.EmployeeId, r.AttendanceDate, r.CheckInAtUtc })
                .ToListAsync(ct))
            .GroupBy(r => (r.EmployeeId, r.AttendanceDate))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var e in checkIns)
        {
            if (e.AttendanceRecordId is not Guid id || !records.TryGetValue(id, out var record)
                || !subjects.TryGetValue(record.EmployeeId, out var person)
                || !locations.TryGetValue(person.LocationId, out var home)
                || record.CheckInAtUtc is not DateTime checkIn)
                continue;

            var day = record.AttendanceDate;
            var shift = EffectiveShift.Resolve(person, plan.ScheduleFor(person.Id, day, person.ScheduleId, schedules), home);
            var dayOff =
                holidays.Any(h => h.Date == day && (h.LocationId == null || h.LocationId == person.LocationId))
                || leaves.Any(l => l.EmployeeId == person.Id && l.FromDate <= day && l.ToDate >= day);
            var earlier = stretches.TryGetValue((person.Id, day), out var same)
                          && same.Any(s => s.Id != record.Id && s.CheckInAtUtc < checkIn);

            if (NotificationLateness.MinutesLate(shift, day, checkIn, _timeZone, dayOff, earlier) is int minutes)
                result[e.Id] = minutes;
        }
        return result;
    }

    public static (string Title, string Body) Describe(string type, string name, int? minutes) => type switch
    {
        NotificationTypes.CheckIn => ("İşçi giriş etdi", SafeText.Truncate($"{name} işə giriş etdi.", 400)),
        NotificationTypes.CheckOut => ("İşçi çıxış etdi", SafeText.Truncate($"{name} işdən çıxış etdi.", 400)),
        NotificationTypes.Late => ("İşçi gecikdi", SafeText.Truncate($"{name} işə {minutes} dəqiqə gecikdi.", 400)),
        _ => ("QRLog", SafeText.Truncate(name, 400)),
    };

    /// <summary>The push's second line: where and when. The time is the scan's own, in Baku time — for an offline scan
    /// too, marked as such; never the sync's.</summary>
    public static string PushBody(string body, string? branch, DateTime timeLocal, bool offline)
    {
        var where = string.IsNullOrWhiteSpace(branch) ? "" : $"{branch} · ";
        var line = $"{where}{timeLocal:HH:mm}{(offline ? " · oflayn qeyd" : "")}";
        return SafeText.Truncate($"{body}\n{line}", 400);
    }
}
