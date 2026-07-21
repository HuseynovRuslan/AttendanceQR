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
    private readonly ILogger<PushNotifier> _logger;

    public PushNotifier(AppDbContext db, IPushSender sender, ILogger<PushNotifier> logger)
    {
        _db = db;
        _sender = sender;
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

        var dead = new List<Domain.Entities.PushSubscription>();
        var reached = new HashSet<Guid>();

        foreach (var s in subs)
        {
            var alive = await _sender.SendAsync(s.Endpoint, s.P256dh, s.Auth, title, body, url, ct);
            if (alive) reached.Add(s.EmployeeId);
            else dead.Add(s);
        }

        if (dead.Count > 0)
        {
            _db.PushSubscriptions.RemoveRange(dead);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Push: notified {Reached}/{Total} employees, pruned {Dead}", reached.Count, employeeIds.Count, dead.Count);
        return reached.Count;
    }
}
