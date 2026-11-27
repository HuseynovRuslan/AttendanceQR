using System.Threading.Channels;
using AttendanceQR.Api.Notifications;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Notifications;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Runs the notification module, per company, as two loops that never wait on each other:
///   • dispatch — outbox events → center lines, owed pushes, live pop-ups. Woken at once by <see cref="INotificationHint"/>
///     when a check-in commits an event, and otherwise every <see cref="Sweep"/> for anything left behind (a lost hint,
///     a retry that came due, a restart);
///   • push — the owed pushes, their summaries and retries. Woken when a dispatch pass owed somebody a push, and every
///     <see cref="PushSweep"/> for throttled and retried ones coming due.
/// Apart, because a push provider is somebody else's server: when it is slow, pushes wait — the center and the live
/// pop-ups do not. Everything either loop decides is in the database, so a restart only delays.
///
/// Off the scan path entirely: a slow push endpoint or a failing pass delays notifications, never a check-in. One
/// company failing does not stop the others.
/// </summary>
public sealed class NotificationWorker : BackgroundService
{
    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PushSweep = TimeSpan.FromSeconds(5);
    private const int MaxBatchesPerPass = 10;

    private readonly IServiceScopeFactory _scopes;
    private readonly INotificationHint _hint;
    private readonly INotificationLive _live;
    private readonly AppOptions _options;
    private readonly bool _pushConfigured;
    private readonly ILogger<NotificationWorker> _logger;

    // Dispatch → push: «this company now owes pushes». Dropping one costs a PushSweep at most.
    private readonly Channel<Guid> _pushHint = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

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

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.WhenAll(
            LoopAsync(_hint.Reader, Sweep, DispatchTenantAsync, "dispatch", ct),
            LoopAsync(_pushHint.Reader, PushSweep, PushTenantAsync, "push", ct));

    private async Task LoopAsync(
        ChannelReader<Guid> hints, TimeSpan sweep, Func<Guid, CancellationToken, Task> run, string name, CancellationToken ct)
    {
        // Let the host finish starting before the first pass.
        await Task.Yield();
        var lastSweep = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var hinted = new HashSet<Guid>();
                while (hints.TryRead(out var tenantId))
                    hinted.Add(tenantId);

                IEnumerable<Guid> tenants = hinted;
                if (DateTime.UtcNow - lastSweep >= sweep)
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    tenants = (await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct)).Union(hinted);
                    lastSweep = DateTime.UtcNow;
                }

                foreach (var tenantId in tenants)
                    await run(tenantId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification {Loop} sweep failed", name);
            }

            try
            {
                // Wake on a hint, or at the next sweep — whichever is first.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(sweep);
                await hints.WaitToReadAsync(wait.Token);
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

    /// <summary>One company, both halves in turn — the tests' way in. The service runs the halves in their own loops.</summary>
    public async Task<DispatchOutcome> RunTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var outcome = await DispatchTenantAsync(tenantId, ct);
        await PushTenantAsync(tenantId, ct);
        return outcome;
    }

    /// <summary>One company's due events, in batches.</summary>
    private async Task<DispatchOutcome> DispatchTenantAsync(Guid tenantId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // One cheap question before any work: most companies, most minutes, have nothing.
            var now = DateTime.UtcNow;
            if (!await db.NotificationEvents.AnyAsync(
                    e => e.Status == NotificationEventStatus.Pending && e.NextAttemptUtc <= now, ct))
                return DispatchOutcome.None;

            var dispatcher = new NotificationDispatcher(_options, _logger);
            var total = DispatchOutcome.None;
            for (var batch = 0; batch < MaxBatchesPerPass; batch++)
            {
                var (outcome, live) = await dispatcher.ProcessAsync(db, DateTime.UtcNow, ct);
                // Nothing from this batch stays tracked into the next: each save sees only its own rows.
                db.ChangeTracker.Clear();
                if (live.Count > 0)
                    await _live.SendAsync(tenantId, live, ct);
                if (outcome.Pushes > 0)
                    _pushHint.Writer.TryWrite(tenantId);
                total = new DispatchOutcome(
                    total.Events + outcome.Events, total.Notifications + outcome.Notifications,
                    total.Pushes + outcome.Pushes, total.Live + outcome.Live, total.Failed + outcome.Failed);
                if (outcome.Events + outcome.Failed < NotificationDispatcher.BatchSize)
                    break;
            }

            if (total.Any)
                _logger.LogInformation(
                    "Notifications for {Tenant}: {Events} events → {Notifications} lines, {Pushes} pushes owed, {Live} live, {Failed} failed",
                    tenantId, total.Events, total.Notifications, total.Pushes, total.Live, total.Failed);
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

    /// <summary>One company's owed pushes.</summary>
    private async Task PushTenantAsync(Guid tenantId, CancellationToken ct)
    {
        // The notifier saves on its own context (it prunes dead subscriptions), so every person's send gets a scope of
        // its own: one person's failed clean-up never rides into the next person's push, nor into this pass's saves.
        var notifierScopes = new List<IServiceScope>();
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTime.UtcNow;
            if (!await db.NotificationDeliveries.AnyAsync(
                    d => d.Channel == NotificationChannel.Push && d.Status == NotificationDeliveryStatus.Pending && d.NextAttemptUtc <= now, ct))
                return;

            IPushNotifier Notifier()
            {
                var s = _scopes.CreateScope();
                notifierScopes.Add(s);
                s.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                return s.ServiceProvider.GetRequiredService<IPushNotifier>();
            }

            var pushed = await new NotificationPush(_logger).DeliverAsync(db, Notifier, _pushConfigured, now, ct);
            if (pushed > 0)
                _logger.LogInformation("Notifications for {Tenant}: {Pushed} pushes sent", tenantId, pushed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notification pushes for {Tenant} failed", tenantId);
        }
        finally
        {
            foreach (var s in notifierScopes)
                s.Dispose();
        }
    }
}
