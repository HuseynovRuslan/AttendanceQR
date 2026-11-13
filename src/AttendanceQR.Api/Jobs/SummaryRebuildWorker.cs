using AttendanceQR.Application.Reporting;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
    // A plan's stale days (PendingSummaryRebuild) are swept every two minutes; a note younger than a minute is left
    // to the request that wrote it, which rebuilds right after saving.
    private const int PlanSweepEvery = 24;
    private static readonly TimeSpan PlanNoteAge = TimeSpan.FromMinutes(1);

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
        var loops = 0;
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

            if (++loops % PlanSweepEvery == 0)
                await SweepPlanNotesAsync(stoppingToken);

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

    /// <summary>The days a plan change marked stale and whose rebuild has not held yet — tried again, company by company.</summary>
    private async Task SweepPlanNotesAsync(CancellationToken ct)
    {
        try
        {
            List<Guid> tenants;
            using (var scope = _scopeFactory.CreateScope())
                tenants = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants
                    .Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
            foreach (var tenantId in tenants)
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var dates = await PlanRebuilds.WaitingAsync(db, PlanNoteAge, ct);
                if (dates.Count == 0)
                    continue;
                var (rebuilt, pending) = await PlanRebuilds.RunAsync(
                    db, scope.ServiceProvider.GetRequiredService<IDailySummaryService>(), dates, _logger, ct);
                _logger.LogInformation("SummaryRebuild: plan days for tenant {Tenant}: {Rebuilt} rebuilt, {Pending} still waiting", tenantId, rebuilt, pending);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SummaryRebuild: sweeping the plan's stale days failed; next sweep tries again");
        }
    }
}
