using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AttendanceQR.Infrastructure.Persistence;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>Sends a notification to employees by id, fanning out over each one's subscriptions and
/// pruning the dead ones. The one place that knows how "notify this person" works.</summary>
public interface IPushNotifier
{
    /// <summary>Returns how many employees were actually reached (had at least one live subscription).</summary>
    Task<int> NotifyEmployeesAsync(
        IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default);
}

public sealed class PushNotifier : IPushNotifier
{
    private readonly AppDbContext _db;
    private readonly IPushSender _sender;
    private readonly IFcmSender _fcm;
    private readonly ILogger<PushNotifier> _logger;

    public PushNotifier(AppDbContext db, IPushSender sender, IFcmSender fcm, ILogger<PushNotifier> logger)
    {
        _db = db;
        _sender = sender;
        _fcm = fcm;
        _logger = logger;
    }

    public async Task<int> NotifyEmployeesAsync(
        IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            return 0;

        var subs = await _db.PushSubscriptions
            .Where(s => employeeIds.Contains(s.EmployeeId))
            .ToListAsync(ct);
        if (subs.Count == 0)
            return 0;

        var dead = new System.Collections.Concurrent.ConcurrentBag<Domain.Entities.PushSubscription>();
        var reached = new System.Collections.Concurrent.ConcurrentDictionary<Guid, byte>();

        // Bounded-parallel fan-out with a per-send timeout. Sequential sends were fine at 95
        // subscriptions; at a 2000-employee tenant a shift-start reminder sweep took longer than the
        // 10-minute lead it exists to give, and one hung push endpoint stalled everyone behind it.
        // Push endpoints are independent I/O — 16 in flight is gentle on the senders' HttpClients.
        // The DbContext is NOT touched inside the parallel body; pruning happens after, on one thread.
        await Parallel.ForEachAsync(
            subs,
            new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
            async (s, token) =>
            {
                using var perSend = CancellationTokenSource.CreateLinkedTokenSource(token);
                perSend.CancelAfter(TimeSpan.FromSeconds(10));
                bool alive;
                try
                {
                    // A native app registration carries an FcmToken and goes out over FCM; a browser/PWA
                    // row has the Web Push keys and goes over Web Push. Same "false = prune" contract.
                    alive = !string.IsNullOrEmpty(s.FcmToken)
                        ? await _fcm.SendAsync(s.FcmToken, title, body, url, perSend.Token)
                        : await _sender.SendAsync(s.Endpoint, s.P256dh, s.Auth, title, body, url, perSend.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    // Timed out, not proven dead — keep the subscription, just skip it this round.
                    alive = true;
                }
                if (alive) reached.TryAdd(s.EmployeeId, 0);
                else dead.Add(s);
            });

        if (!dead.IsEmpty)
        {
            _db.PushSubscriptions.RemoveRange(dead);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Push: notified {Reached}/{Total} employees, pruned {Dead}", reached.Count, employeeIds.Count, dead.Count);
        return reached.Count;
    }
}
