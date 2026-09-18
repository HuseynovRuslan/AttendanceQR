using AttendanceQR.Application.Reporting;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Services;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Rebuilds the stored summary of a past day a scan has just landed on — see
/// <see cref="ISummaryRebuildQueue"/> for why the scan path cannot leave it to the nightly job.
///
/// Polls every few seconds and rebuilds each requested day once it has been quiet for a moment, so a
/// crew phone replaying thirty taps onto yesterday costs one rebuild, not thirty. Today is never
/// rebuilt: GenerateForDateAsync refuses it by design (today is computed live everywhere).
/// </summary>
public sealed class SummaryRebuildWorker : BackgroundService
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private readonly ISummaryRebuildQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SummaryRebuildWorker> _logger;

    public SummaryRebuildWorker(ISummaryRebuildQueue queue, IServiceScopeFactory scopeFactory, ILogger<SummaryRebuildWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            foreach (var (tenantId, date) in _queue.TakeSettled(Settle, DateTime.UtcNow))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                    var count = await scope.ServiceProvider.GetRequiredService<IDailySummaryService>()
                        .GenerateForDateAsync(date, stoppingToken);
                    _logger.LogInformation("SummaryRebuild: {Date} rebuilt for tenant {Tenant} ({Count} rows) after a late scan",
                        date, tenantId, count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never fatal: the record itself is written, and the day can be rebuilt by hand
                    // («Günləri yenidən hesabla»). Asking again gives it another turn on the next scan.
                    _logger.LogError(ex, "SummaryRebuild: {Date} failed for tenant {Tenant}", date, tenantId);
                }
            }
        }
    }
}
