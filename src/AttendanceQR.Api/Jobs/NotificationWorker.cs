using AttendanceQR.Api.Notifications;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Runs the notification module: per company, the dispatcher (outbox events → notifications) and then the push
/// sender (owed pushes, retries, summaries). Woken at once by <see cref="INotificationHint"/> when a check-in commits
/// an event, and otherwise every <see cref="Sweep"/> for anything left behind — a lost hint, a retry that came due,
/// a restart. Everything it decides is in the database, so a restart only delays.
///
/// Off the scan path entirely: a slow push endpoint or a failing pass delays notifications, never a check-in. One
/// company failing does not stop the others.
/// </summary>
public sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(15);
    private const int MaxBatchesPerPass = 10;

    private readonly IServiceScopeFactory _scopes;
    private readonly INotificationHint _hint;
    private readonly INotificationLive _live;
    private readonly AppOptions _options;
    private readonly bool _pushConfigured;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(
        IServiceScopeFactory scopes, INotificationHint hint, INotificationLive live, AppOptions options,
        PushOptions push, FcmOptions fcm, ILogger<NotificationWorker> logger)
    {
        _scopes = scopes;
        _hint = hint;
        _live = live;
        _options = options;
        // Either channel is enough: the notifier picks FCM for an app token and Web Push for a browser.
        _pushConfigured = push.IsConfigured || fcm.IsConfigured;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var lastSweep = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var hinted = new HashSet<Guid>();
                while (_hint.Reader.TryRead(out var tenantId))
                    hinted.Add(tenantId);

                IEnumerable<Guid> tenants = hinted;
                if (DateTime.UtcNow - lastSweep >= Sweep)
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    tenants = (await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct)).Union(hinted);
                    lastSweep = DateTime.UtcNow;
                }

                foreach (var tenantId in tenants)
                    await RunTenantAsync(tenantId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification sweep failed");
            }

            try
            {
                // Wake on a hint, or at the next sweep — whichever is first.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(Sweep);
                await _hint.Reader.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // the sweep interval passed
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One company: dispatch everything due (in batches), then send the owed pushes. Public for the tests.</summary>
    public async Task<DispatchOutcome> RunTenantAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Two cheap questions before any work: most companies, most minutes, have nothing.
            var now = DateTime.UtcNow;
            var hasEvents = await db.NotificationEvents.AnyAsync(
                e => e.Status == NotificationEventStatus.Pending && e.NextAttemptUtc <= now, ct);
            var hasPushes = await db.NotificationDeliveries.AnyAsync(
                d => d.Channel == NotificationChannel.Push && d.Status == NotificationDeliveryStatus.Pending && d.NextAttemptUtc <= now, ct);
            if (!hasEvents && !hasPushes)
                return DispatchOutcome.None;

            var dispatcher = new NotificationDispatcher(_options, _logger);
            var total = DispatchOutcome.None;
            for (var batch = 0; hasEvents && batch < MaxBatchesPerPass; batch++)
            {
                var (outcome, live) = await dispatcher.ProcessAsync(db, DateTime.UtcNow, ct);
                if (live.Count > 0)
                    await _live.SendAsync(tenantId, live, ct);
                total = new DispatchOutcome(
                    total.Events + outcome.Events, total.Notifications + outcome.Notifications,
                    total.Pushes + outcome.Pushes, total.Live + outcome.Live, total.Failed + outcome.Failed);
                if (outcome.Events < NotificationDispatcher.BatchSize)
                    break;
            }

            // The notifier saves on its own context (it prunes dead subscriptions), so it gets its own scope:
            // its save must never carry this pass's half-finished changes.
            using var pushScope = _scopes.CreateScope();
            pushScope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
            var notifier = pushScope.ServiceProvider.GetRequiredService<IPushNotifier>();
            var pushed = await new NotificationPush(_logger).DeliverAsync(db, () => notifier, _pushConfigured, DateTime.UtcNow, ct);

            if (total.Any || pushed > 0)
                _logger.LogInformation(
                    "Notifications for {Tenant}: {Events} events → {Notifications} lines, {Pushes} pushes owed, {Live} live, {Pushed} pushes sent, {Failed} failed",
                    tenantId, total.Events, total.Notifications, total.Pushes, total.Live, pushed, total.Failed);
            return total;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notifications for {Tenant} failed", tenantId);
            return DispatchOutcome.None;
        }
    }
}
