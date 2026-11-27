using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AttendanceQR.Infrastructure.Persistence;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>What became of a notification for one employee, over all of their devices.</summary>
public enum PushDelivery
{
    /// <summary>At least one of their devices' push services accepted it.</summary>
    Delivered,

    /// <summary>Nothing to send to: no subscription, every one turned out gone, or push is not set up.</summary>
    NoDevice,

    /// <summary>Not delivered anywhere, and at least one device failed in a way a later attempt may fix
    /// (timeout, 429, 5xx, network). Worth retrying; never to be reported as delivered.</summary>
    Transient,

    /// <summary>Not delivered anywhere, nothing transient: the push services refused it (bad configuration or
    /// payload). A retry would be refused the same way.</summary>
    Rejected,
}

/// <summary>Sends a notification to employees by id, fanning out over each one's subscriptions and
/// pruning the dead ones. The one place that knows how "notify this person" works.</summary>
public interface IPushNotifier
{
    /// <summary>Returns how many employees it was delivered to (at least one device accepted). A device that timed
    /// out or answered 429/5xx does not count.</summary>
    Task<int> NotifyEmployeesAsync(
        IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default);

    /// <summary>The outcome for each of <paramref name="employeeIds"/> (each distinct id once). For a caller that
    /// keeps its own retry — <see cref="PushDelivery.Transient"/> is the one worth another attempt.</summary>
    /// <remarks>The default serves implementations that can only count (test doubles): one call per employee.</remarks>
    async Task<IReadOnlyDictionary<Guid, PushDelivery>> NotifyEachAsync(
        IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
    {
        var outcome = new Dictionary<Guid, PushDelivery>();
        foreach (var id in employeeIds.Distinct())
            outcome[id] = await NotifyEmployeesAsync([id], title, body, url, ct) > 0 ? PushDelivery.Delivered : PushDelivery.NoDevice;
        return outcome;
    }
}

public sealed class PushNotifier : IPushNotifier
{
    /// <summary>How long one device may take before it counts as Transient for this round.</summary>
    public static readonly TimeSpan PerSendTimeout = TimeSpan.FromSeconds(10);

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
        var outcome = await NotifyEachAsync(employeeIds, title, body, url, ct);
        return outcome.Values.Count(o => o == PushDelivery.Delivered);
    }

    public async Task<IReadOnlyDictionary<Guid, PushDelivery>> NotifyEachAsync(
        IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
    {
        var people = employeeIds.Distinct().ToList();
        if (people.Count == 0)
            return new Dictionary<Guid, PushDelivery>();

        var subs = await _db.PushSubscriptions
            .Where(s => people.Contains(s.EmployeeId))
            .ToListAsync(ct);

        var results = new System.Collections.Concurrent.ConcurrentBag<(Domain.Entities.PushSubscription Sub, PushSendResult Result)>();

        // Bounded-parallel fan-out with a per-send timeout. Sequential sends were fine at 95
        // subscriptions; at a 2000-employee tenant a shift-start reminder sweep took longer than the
        // 10-minute lead it exists to give, and one hung push endpoint stalled everyone behind it.
        // Push endpoints are independent I/O — 16 in flight is gentle on the senders' HttpClients.
        // The DbContext is NOT touched inside the parallel body; pruning happens after, on one thread.
        if (subs.Count > 0)
        {
            await Parallel.ForEachAsync(
                subs,
                new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
                async (s, token) =>
                {
                    using var perSend = CancellationTokenSource.CreateLinkedTokenSource(token);
                    perSend.CancelAfter(PerSendTimeout);
                    PushSendResult result;
                    try
                    {
                        // A native app registration carries an FcmToken and goes out over FCM; a browser/PWA
                        // row has the Web Push keys and goes over Web Push. Same outcome contract.
                        result = !string.IsNullOrEmpty(s.FcmToken)
                            ? await _fcm.SendAsync(s.FcmToken, title, body, url, perSend.Token)
                            : await _sender.SendAsync(s.Endpoint, s.P256dh, s.Auth, title, body, url, perSend.Token);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        // Timed out: not proven dead, and not delivered either — keep it, count it as worth a retry.
                        result = PushSendResult.Transient;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The senders do not throw for a failed send; this is a bug of ours. Keep the row, retryable.
                        _logger.LogError(ex, "Push: sender threw");
                        result = PushSendResult.Transient;
                    }
                    results.Add((s, result));
                });
        }

        var outcome = people.ToDictionary(id => id, _ => PushDelivery.NoDevice);
        foreach (var group in results.GroupBy(r => r.Sub.EmployeeId))
            outcome[group.Key] = Combine(group.Select(r => r.Result));

        var dead = results.Where(r => r.Result == PushSendResult.Gone).Select(r => r.Sub).ToList();
        if (dead.Count > 0)
        {
            _db.PushSubscriptions.RemoveRange(dead);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Push: delivered to {Delivered}/{Total} employees, transient {Transient}, refused {Rejected}, pruned {Dead}",
            outcome.Values.Count(o => o == PushDelivery.Delivered), people.Count,
            outcome.Values.Count(o => o == PushDelivery.Transient),
            outcome.Values.Count(o => o == PushDelivery.Rejected), dead.Count);
        return outcome;
    }

    /// <summary>One employee's devices, summed up: delivered anywhere wins; then a failure worth retrying; then a
    /// refusal; nothing attempted (gone, not configured) is no device.</summary>
    public static PushDelivery Combine(IEnumerable<PushSendResult> devices)
    {
        var all = devices as ICollection<PushSendResult> ?? devices.ToList();
        if (all.Contains(PushSendResult.Delivered)) return PushDelivery.Delivered;
        if (all.Contains(PushSendResult.Transient)) return PushDelivery.Transient;
        if (all.Contains(PushSendResult.Rejected)) return PushDelivery.Rejected;
        return PushDelivery.NoDevice;
    }
}
