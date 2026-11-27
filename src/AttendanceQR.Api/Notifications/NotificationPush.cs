using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Notifications;

/// <summary>
/// Sends the pushes the dispatcher owes (<see cref="NotificationDelivery"/>), for one company per call, through the
/// existing <see cref="IPushNotifier"/> — Web Push and FCM, with its own removal of dead subscriptions (a 404/410 or
/// an unregistered token), so there is no second device-token store.
///
/// Person by person, never row by row: a pass takes the people who are owed something (oldest debt first) and settles
/// each one's whole debt at once. A burst is one push, not a ringing phone: a person who was pushed less than
/// <see cref="Throttle"/> ago gets nothing now, and what piles up meanwhile goes out as ONE summary («12 yeni bildiriş:
/// 9 giriş, 3 gecikmə») when the window opens. At shift start an administrator watching a thousand arrivals gets one
/// push a minute, every arrival is still a line in their center — and the branch manager owed a single push is not
/// kept waiting behind the administrator's thousand rows (which a batch of the oldest rows did: measured, 10 000
/// arrivals left most managers' pushes queued for minutes).
///
/// Status per delivery: Sent / Digested (part of a summary) — a push service took it; NoDevice (nobody subscribed —
/// not retried); Skipped; Failed — refused by the push service (a retry would be refused the same way), or still not
/// taken after <see cref="MaxAttempts"/> tries. A timeout, a 429 or a 5xx is not a send: the rows stay Pending and are
/// tried again with a growing delay (<see cref="NotificationDispatcher.Backoff"/>). Each person's outcome is saved the moment their
/// push returns, so a pass cut short (a deploy's stop, a failing save) re-sends at most the one push that was in
/// flight — never a whole batch. A pass stops starting new people after <see cref="PassBudget"/>, so a slow push
/// provider delays pushes and nothing else.
/// </summary>
public sealed class NotificationPush
{
    public const int MaxPeoplePerPass = 200;

    /// <summary>The most rows one person's push settles in a pass; a larger backlog is the next summary's.</summary>
    public const int MaxRowsPerPerson = 5000;

    public const int MaxAttempts = 6;
    public static readonly TimeSpan Throttle = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan PassBudget = TimeSpan.FromSeconds(20);

    private readonly ILogger _logger;
    private readonly TimeProvider _clock;

    public NotificationPush(ILogger logger, TimeProvider? clock = null)
    {
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <param name="notifier">Called once per person pushed — the caller may hand out a fresh notifier each time, so one
    /// person's subscription clean-up can never carry into the next person's send.</param>
    public async Task<int> DeliverAsync(
        AppDbContext db, Func<IPushNotifier> notifier, bool configured, DateTime nowUtc, CancellationToken ct = default)
    {
        IQueryable<NotificationDelivery> Due() => db.NotificationDeliveries
            .Where(d => d.Channel == NotificationChannel.Push && d.Status == NotificationDeliveryStatus.Pending
                        && d.NextAttemptUtc <= nowUtc);

        // Who is owed, oldest debt first — one row per person however many rows they are owed.
        var people = (await Due()
                .GroupBy(d => d.RecipientEmployeeId)
                .Select(g => new { Recipient = g.Key, Oldest = g.Min(d => d.CreatedAtUtc) })
                .OrderBy(g => g.Oldest)
                .Take(MaxPeoplePerPass)
                .ToListAsync(ct))
            .Select(g => g.Recipient)
            .ToList();
        if (people.Count == 0)
            return 0;

        var since = nowUtc - Throttle;
        var lastSent = (await db.NotificationDeliveries.AsNoTracking()
                .Where(d => people.Contains(d.RecipientEmployeeId) && d.Channel == NotificationChannel.Push
                            && d.SentAtUtc != null && d.SentAtUtc > since)
                .Select(d => new { d.RecipientEmployeeId, d.SentAtUtc })
                .ToListAsync(ct))
            .GroupBy(d => d.RecipientEmployeeId)
            .ToDictionary(g => g.Key, g => g.Max(d => d.SentAtUtc!.Value));

        // Only accounts that can still receive anything: an account deactivated since the event gets no push.
        var active = (await db.Employees.AsNoTracking()
                .Where(e => people.Contains(e.Id) && e.IsActive)
                .Select(e => e.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var started = _clock.GetTimestamp();
        var sent = 0;
        foreach (var person in people)
        {
            if (_clock.GetElapsedTime(started) > PassBudget)
                break;

            var items = await Due()
                .Where(d => d.RecipientEmployeeId == person)
                .OrderBy(d => d.CreatedAtUtc)
                .Take(MaxRowsPerPerson)
                .ToListAsync(ct);
            if (items.Count == 0)
                continue;

            if (!configured || !active.Contains(person))
            {
                foreach (var d in items)
                {
                    d.Status = NotificationDeliveryStatus.Skipped;
                    d.LastError = configured ? "Inactive" : "PushNotConfigured";
                }
            }
            else if (lastSent.TryGetValue(person, out var last) && last > since)
            {
                foreach (var d in items)
                    d.NextAttemptUtc = last + Throttle;
            }
            else
            {
                var (title, body, url) = items.Count == 1 ? (items[0].Title, items[0].Body, items[0].Url)
                    : AboutOnePerson(items) is { } one ? (one.Title, one.Body, one.Url)
                    : Digest(items);
                try
                {
                    var outcome = await notifier().NotifyEachAsync([person], title, body, url, ct);
                    var result = outcome.TryGetValue(person, out var r) ? r : PushDelivery.NoDevice;
                    if (result is PushDelivery.Delivered or PushDelivery.NoDevice)
                    {
                        var status = result == PushDelivery.NoDevice ? NotificationDeliveryStatus.NoDevice
                            : items.Count == 1 ? NotificationDeliveryStatus.Sent
                            : NotificationDeliveryStatus.Digested;
                        foreach (var d in items)
                        {
                            d.Status = status;
                            d.Attempts++;
                            d.SentAtUtc = nowUtc;
                            d.LastError = null;
                        }
                        if (result == PushDelivery.Delivered)
                            sent++;
                    }
                    else
                    {
                        // Not delivered. Transient (a timeout, 429, 5xx): pending, tried again later. Rejected: the push
                        // service refused it for a reason a retry will not change — failed now, not six times.
                        foreach (var d in items)
                        {
                            d.Attempts++;
                            d.LastError = result == PushDelivery.Transient
                                ? "Transient: the push service did not take it (timeout, 429 or 5xx)"
                                : "Rejected: the push service refused it";
                            if (result == PushDelivery.Rejected || d.Attempts >= MaxAttempts)
                                d.Status = NotificationDeliveryStatus.Failed;
                            else
                                d.NextAttemptUtc = nowUtc + NotificationDispatcher.Backoff(d.Attempts);
                        }
                        _logger.LogWarning("Notifications: push to {Recipient} not delivered ({Result}, {Count} waiting)",
                            person, result, items.Count);
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    foreach (var d in items)
                    {
                        d.Attempts++;
                        d.LastError = SafeText.Truncate($"{ex.GetType().Name}: {ex.Message}", 500);
                        if (d.Attempts >= MaxAttempts)
                            d.Status = NotificationDeliveryStatus.Failed;
                        else
                            d.NextAttemptUtc = nowUtc + NotificationDispatcher.Backoff(d.Attempts);
                    }
                    _logger.LogWarning(ex, "Notifications: push to {Recipient} failed ({Count} waiting)", person, items.Count);
                }
            }

            // This person's outcome is kept before anybody else is tried — even when the pass is being stopped, since
            // the push itself has already gone.
            await db.SaveChangesAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
        }
        return sent;
    }

    /// <summary>
    /// Several rows that are all about one person — a late arrival is two (the check-in and the lateness): their own
    /// sentence, not a count. «Əliyev Elvin işə 25 dəqiqə gecikdi» says what «2 yeni bildiriş: 1 giriş, 1 gecikmə» makes
    /// the reader open the app to find out. The lateness wins (it says the arrival too), otherwise the latest row.
    /// </summary>
    public static NotificationDelivery? AboutOnePerson(IReadOnlyList<NotificationDelivery> items)
    {
        var person = items[0].Url;
        if (string.IsNullOrEmpty(person) || !person.StartsWith("/admin/employees/", StringComparison.Ordinal)
            || items.Any(d => d.Url != person))
            return null;
        return items.LastOrDefault(d => d.TypeKey == NotificationTypes.Late) ?? items[^1];
    }

    /// <summary>One push for several: how many, and of what — never whose (the center has the names).</summary>
    public static (string Title, string Body, string Url) Digest(IReadOnlyCollection<NotificationDelivery> items)
    {
        var parts = new List<string>();
        void Count(string type, string label)
        {
            var n = items.Count(d => d.TypeKey == type);
            if (n > 0) parts.Add($"{n} {label}");
        }
        Count(NotificationTypes.CheckIn, "giriş");
        Count(NotificationTypes.CheckOut, "çıxış");
        Count(NotificationTypes.Late, "gecikmə");
        var body = $"{items.Count} yeni bildiriş" + (parts.Count > 0 ? ": " + string.Join(", ", parts) : "");
        return ("Davamiyyət bildirişləri", body, "/admin/notifications");
    }
}
