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
/// A burst is one push, not a ringing phone: a person who was pushed less than <see cref="Throttle"/> ago gets nothing
/// now, and what piles up meanwhile goes out as ONE summary («12 yeni bildiriş: 9 giriş, 3 gecikmə») when the window
/// opens. At shift start an administrator watching a thousand arrivals gets one push a minute, and every arrival is
/// still a line in their center.
///
/// Status per delivery: Sent / Digested (part of a summary) / NoDevice (nobody subscribed — not retried) / Failed after
/// <see cref="MaxAttempts"/> tries with a growing delay. A send is marked before it is attempted again, never twice.
/// </summary>
public sealed class NotificationPush
{
    public const int BatchSize = 1000;
    public const int MaxAttempts = 6;
    public static readonly TimeSpan Throttle = TimeSpan.FromSeconds(60);

    private readonly ILogger _logger;

    public NotificationPush(ILogger logger) => _logger = logger;

    public async Task<int> DeliverAsync(
        AppDbContext db, Func<IPushNotifier> notifier, bool configured, DateTime nowUtc, CancellationToken ct = default)
    {
        var due = await db.NotificationDeliveries
            .Where(d => d.Channel == NotificationChannel.Push && d.Status == NotificationDeliveryStatus.Pending
                        && d.NextAttemptUtc <= nowUtc)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0)
            return 0;

        if (!configured)
        {
            foreach (var d in due)
                d.Status = NotificationDeliveryStatus.Skipped;
            await db.SaveChangesAsync(ct);
            return 0;
        }

        var people = due.Select(d => d.RecipientEmployeeId).Distinct().ToList();
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

        var sent = 0;
        foreach (var group in due.GroupBy(d => d.RecipientEmployeeId))
        {
            var items = group.ToList();
            if (!active.Contains(group.Key))
            {
                foreach (var d in items)
                {
                    d.Status = NotificationDeliveryStatus.Skipped;
                    d.LastError = "Inactive";
                }
                continue;
            }
            if (lastSent.TryGetValue(group.Key, out var last) && last > since)
            {
                foreach (var d in items)
                    d.NextAttemptUtc = last + Throttle;
                continue;
            }

            var (title, body, url) = items.Count == 1 ? (items[0].Title, items[0].Body, items[0].Url) : Digest(items);
            try
            {
                var reached = await notifier().NotifyEmployeesAsync([group.Key], title, body, url, ct);
                var status = reached == 0 ? NotificationDeliveryStatus.NoDevice
                    : items.Count == 1 ? NotificationDeliveryStatus.Sent
                    : NotificationDeliveryStatus.Digested;
                foreach (var d in items)
                {
                    d.Status = status;
                    d.Attempts++;
                    d.SentAtUtc = nowUtc;
                    d.LastError = null;
                }
                if (reached > 0)
                    sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
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
                _logger.LogWarning(ex, "Notifications: push to {Recipient} failed ({Count} waiting)", group.Key, items.Count);
            }
        }
        await db.SaveChangesAsync(ct);
        return sent;
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
