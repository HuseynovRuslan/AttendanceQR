using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Delivers staff alerts once they are due — a refusal after its grace period, a request at once, and
/// anything due in the night at the end of quiet hours. See StaffAlertKinds for what is alerted and when,
/// and StaffAlertDispatcher for who hears it.
///
/// Alerts still waiting are held in memory; a restart forgets them. That costs a courtesy, never a
/// record — the refusal itself is already in the audit log and on the Problems screen.
/// </summary>
public sealed class StaffAlertWorker : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    // A night's alerts now wait here until the morning, so the list is bounded as the channel is: the
    // oldest go first, and each person is alerted about each problem once a day anyway.
    private const int MaxWaiting = 2000;

    private readonly IStaffAlertQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<StaffAlertWorker> _logger;

    public StaffAlertWorker(
        IStaffAlertQueue queue, IServiceScopeFactory scopes, TimeProvider clock, AppOptions options,
        ILogger<StaffAlertWorker> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _clock = clock;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var waiting = new List<StaffAlert>();
        while (!ct.IsCancellationRequested)
        {
            while (_queue.Reader.TryRead(out var alert))
                waiting.Add(alert);
            if (waiting.Count > MaxWaiting)
                waiting.RemoveRange(0, waiting.Count - MaxWaiting);

            var now = _clock.GetUtcNow().UtcDateTime;
            foreach (var alert in waiting.Where(a => StaffAlertKinds.DueAtUtc(a, _timeZone) <= now).ToList())
            {
                waiting.Remove(alert);
                await DeliverAsync(alert, now, ct);
            }

            // Wake for a new alert, or every Tick to release the ones whose wait has run out.
            using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wake.CancelAfter(Tick);
            try
            {
                if (!await _queue.Reader.WaitToReadAsync(wake.Token))
                    await Task.Delay(Tick, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The tick, not a shutdown.
            }
        }
    }

    private async Task DeliverAsync(StaffAlert alert, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(alert.TenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifier = scope.ServiceProvider.GetRequiredService<IPushNotifier>();
            var outcome = await StaffAlertDispatcher.HandleAsync(db, notifier, alert, nowUtc, _timeZone, ct);
            _logger.LogInformation("Staff alert {Kind} for {EmployeeId}: {Outcome}", alert.Kind, alert.EmployeeId, outcome);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // One alert failing must not stop the next one, and must never surface anywhere a scan can see.
            _logger.LogWarning(ex, "Staff alert {Kind} for {EmployeeId} failed", alert.Kind, alert.EmployeeId);
        }
    }
}
