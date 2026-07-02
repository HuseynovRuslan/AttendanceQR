using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Nightly job that materializes the previous day's <see cref="Domain.Entities.DailySummary"/> rows.
/// Lives in the Api (composition root) rather than Infrastructure on purpose: it invokes
/// <see cref="IDailySummaryService"/> from the Application layer, and Application already references
/// Infrastructure — hosting the job in Infrastructure would create a project-reference cycle.
///
/// A plain BackgroundService is deliberate: at this scale Hangfire/Quartz (persistent queues,
/// dashboards, distributed scheduling) are overkill. One timer computing the next 00:30 local is enough.
/// </summary>
public sealed class DailySummaryJob : BackgroundService
{
    private const int BackfillCapDays = 60;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailySummaryJob> _logger;
    private readonly TimeZoneInfo _timeZone;

    public DailySummaryJob(IServiceScopeFactory scopeFactory, AppOptions options, ILogger<DailySummaryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Catch up on any days missed while the server was down.
        await BackfillAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.LogInformation("DailySummaryJob: next run in {Delay}", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await GenerateAsync(Yesterday(), stoppingToken);
        }
    }

    // Runs yesterday because by 00:30 the previous day's check-outs are all in.
    private DateOnly Yesterday()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        return DateOnly.FromDateTime(nowLocal.Date.AddDays(-1));
    }

    private TimeSpan TimeUntilNextRun()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        var todayRun = nowLocal.Date.AddMinutes(30); // 00:30 local
        var nextLocal = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), _timeZone);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    // Simple gap-fill: from the day after the last stored summary up to yesterday, capped so a long
    // outage can't spin for years. If nothing is stored yet, just do yesterday.
    private async Task BackfillAsync(CancellationToken ct)
    {
        try
        {
            var yesterday = Yesterday();
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var summaries = scope.ServiceProvider.GetRequiredService<IDailySummaryService>();

            var lastDate = await db.DailySummaries
                .OrderByDescending(s => s.SummaryDate)
                .Select(s => (DateOnly?)s.SummaryDate)
                .FirstOrDefaultAsync(ct);

            var start = lastDate is DateOnly d ? d.AddDays(1) : yesterday;
            if (start > yesterday)
                return;
            if (yesterday.DayNumber - start.DayNumber > BackfillCapDays)
                start = yesterday.AddDays(-BackfillCapDays);

            for (var day = start; day <= yesterday; day = day.AddDays(1))
                await summaries.GenerateForDateAsync(day, ct);

            _logger.LogInformation("DailySummaryJob: backfilled {From}..{To}", start, yesterday);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DailySummaryJob: backfill failed");
        }
    }

    private async Task GenerateAsync(DateOnly date, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var summaries = scope.ServiceProvider.GetRequiredService<IDailySummaryService>();
            var count = await summaries.GenerateForDateAsync(date, ct);
            _logger.LogInformation("DailySummaryJob: generated {Count} summaries for {Date}", count, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DailySummaryJob: generation failed for {Date}", date);
        }
    }
}
