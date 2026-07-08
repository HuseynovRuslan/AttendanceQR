using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Daily retention job for photo audit: deletes check-in selfies older than
/// <see cref="MinioOptions.RetentionDays"/> from the <c>checkins/</c> prefix in MinIO. Reference
/// selfies (<c>reference/</c>) are never touched. Deliberately a plain <see cref="BackgroundService"/>
/// with a local-timezone timer — the same pattern as <see cref="DailySummaryJob"/>, no Hangfire.
/// Runs at 01:00 local, just after the 00:30 summary job so the two don't overlap.
/// </summary>
public sealed class PhotoCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MinioOptions _minioOptions;
    private readonly ILogger<PhotoCleanupJob> _logger;
    private readonly TimeZoneInfo _timeZone;

    public PhotoCleanupJob(
        IServiceScopeFactory scopeFactory,
        IOptions<MinioOptions> minioOptions,
        AppOptions appOptions,
        ILogger<PhotoCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _minioOptions = minioOptions.Value;
        _logger = logger;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Nothing to prune if storage isn't configured (e.g. local dev without MinIO).
        if (string.IsNullOrWhiteSpace(_minioOptions.Endpoint))
        {
            _logger.LogInformation("PhotoCleanupJob: storage not configured (Storage:Minio:Endpoint empty) — job idle.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.LogInformation("PhotoCleanupJob: next run in {Delay}", delay);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_minioOptions.RetentionDays);
            using var scope = _scopeFactory.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IPhotoStorageService>();
            await storage.DeleteByPrefixOlderThanAsync(MinioPhotoStorageService.CheckInPrefix, cutoff, ct);
            _logger.LogInformation("PhotoCleanupJob: pruned check-in photos older than {Cutoff:o}", cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PhotoCleanupJob: cleanup run failed");
        }
    }

    // 01:00 local — after DailySummaryJob's 00:30.
    private TimeSpan TimeUntilNextRun()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        var todayRun = nowLocal.Date.AddHours(1); // 01:00 local
        var nextLocal = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), _timeZone);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
