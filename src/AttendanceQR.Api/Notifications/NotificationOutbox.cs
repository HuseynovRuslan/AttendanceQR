using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api.Notifications;

/// <summary>
/// The producer side of the notification module: puts an outbox row (<see cref="NotificationEvent"/>) into the caller's
/// change, so it commits in the same save as the check-in or check-out it describes — or not at all.
///
/// Everything costly happens later, in <c>NotificationWorker</c>: who receives it, whether it was late, the push.
/// The scan pays for one cached «does anybody here want this» lookup and one small insert, and only in a company where
/// somebody has switched attendance notifications on; everywhere else nothing is written. The cache is short (30 s)
/// and cleared when a person changes their settings (<see cref="Forget"/>), so switching on takes effect at once.
///
/// It never throws into the scan: a failed lookup writes no event (a missed notification), never a refused check-in.
/// </summary>
public sealed class NotificationOutbox
{
    private static readonly TimeSpan WantsFor = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;
    private readonly INotificationHint? _hint;
    private readonly ILogger<NotificationOutbox> _logger;

    public NotificationOutbox(IMemoryCache cache, ILogger<NotificationOutbox> logger, INotificationHint? hint = null)
    {
        _cache = cache;
        _logger = logger;
        _hint = hint;
    }

    private static string Key(Guid tenantId) => $"notif-wants:{tenantId:N}";
    private static string GenerationKey(Guid tenantId) => $"notif-wants-gen:{tenantId:N}";

    private static long Generation(IMemoryCache cache, Guid tenantId) =>
        cache.TryGetValue(GenerationKey(tenantId), out long g) ? g : 0;

    /// <summary>
    /// Drops the cached answer for a company — after somebody changed their settings. Also moves the company's
    /// generation on, so a lookup that was already under way when the settings changed (and read them before) cannot
    /// put its stale «nobody» back into the cache for the next 30 s.
    /// </summary>
    public static void Forget(IMemoryCache cache, Guid tenantId)
    {
        cache.Set(GenerationKey(tenantId), Generation(cache, tenantId) + 1);
        cache.Remove(Key(tenantId));
    }

    /// <summary>
    /// Adds the event to <paramref name="db"/>'s change (does not save). Returns it, or null when nobody in the company
    /// wants attendance notifications. The caller detaches it if its own save is abandoned.
    /// </summary>
    public async Task<NotificationEvent?> StageAsync(
        AppDbContext db, string kind, AttendanceRecord record, DateTime occurredAtUtc, CancellationToken ct = default)
    {
        try
        {
            var tenantId = db.CurrentTenantId;
            if (!await WantsAsync(db, tenantId, ct))
                return null;

            var e = new NotificationEvent
            {
                Kind = kind,
                SubjectEmployeeId = record.EmployeeId,
                AttendanceRecordId = record.Id,
                LocationId = record.LocationId,
                OccurredAtUtc = occurredAtUtc,
                WasOffline = record.WasOffline,
            };
            db.NotificationEvents.Add(e);
            return e;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Notification outbox: no event for record {RecordId} ({Kind})", record.Id, kind);
            return null;
        }
    }

    /// <summary>Tells the worker a company has fresh events — after the caller's save. Losing it costs one poll interval.</summary>
    public void Hint(Guid tenantId) => _hint?.Ready(tenantId);

    private async Task<bool> WantsAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
    {
        if (_cache.TryGetValue(Key(tenantId), out bool wants))
            return wants;
        var generation = Generation(_cache, tenantId);
        wants = await db.NotificationPreferences.AsNoTracking()
            .AnyAsync(p => p.Category == NotificationTypes.CategoryAttendance && p.EnabledTypes != "", ct);
        // Kept only if nobody changed their settings while the question was being asked.
        if (Generation(_cache, tenantId) == generation)
            _cache.Set(Key(tenantId), wants, WantsFor);
        return wants;
    }
}
