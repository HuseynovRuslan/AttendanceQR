using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Nightly retention for the three ledger tables every scan writes to and nothing ever pruned:
/// ProcessedScans, EmployeeNotifications, AuditLogs. At 2000 employees they grow ~5M rows/year
/// combined; their useful lives are 18 hours, ~a season, and ~a year respectively. Same plain
/// BackgroundService + local-timezone timer pattern as <see cref="DailySummaryJob"/>; runs at 01:30
/// local, after the 00:30 summaries and the 01:00 photo cleanup.
///
/// AttendanceRecords and DailySummaries are deliberately NOT here — they are the attendance ledger
/// itself (pay disputes, tabel history) and are never aged out.
/// </summary>
public sealed class DataRetentionJob : BackgroundService
{
    // ProcessedScans exist only to deduplicate replayed offline scans, and the offline trust window
    // is 18 hours — a week is 9x that; nothing older can ever be replayed.
    private const int ProcessedScanDays = 7;

    // Reminders/nudges an employee has long since seen (the bell shows a handful of recent ones).
    private const int EmployeeNotificationDays = 90;

    // Rejected-scan forensics (the problems screen reads 7 days; disputes reach back months). A year
    // covers any argument that ends up mattering, and keeps the table at a bounded steady state.
    private const int AuditLogDays = 365;

    // The notification module. The center is a feed, not a record — the attendance itself is kept for ever in
    // AttendanceRecords — so 90 days of lines; their push bookkeeping a month; a processed outbox event a week (it
    // is only kept to explain a missing notification), a given-up one a month.
    private const int NotificationDays = 90;
    private const int NotificationDeliveryDays = 30;
    private const int NotificationEventDoneDays = 7;
    private const int NotificationEventFailedDays = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataRetentionJob> _logger;
    private readonly TimeZoneInfo _timeZone;

    public DataRetentionJob(IServiceScopeFactory scopeFactory, AppOptions appOptions, ILogger<DataRetentionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            _logger.LogInformation("DataRetentionJob: next run in {Delay}", delay);
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
        var now = DateTime.UtcNow;

        // Per tenant, like every other job: the query filters scope each delete to the resolved
        // tenant, so this never needs (and never gets) IgnoreQueryFilters.
        List<Guid> tenantIds;
        using (var listScope = _scopeFactory.CreateScope())
        {
            var db = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(ct);
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var scans = await db.ProcessedScans
                    .Where(p => p.ProcessedAtUtc < now.AddDays(-ProcessedScanDays))
                    .ExecuteDeleteAsync(ct);
                var notifications = await db.EmployeeNotifications
                    .Where(n => n.CreatedAtUtc < now.AddDays(-EmployeeNotificationDays))
                    .ExecuteDeleteAsync(ct);
                var audits = await db.AuditLogs
                    .Where(a => a.CreatedAtUtc < now.AddDays(-AuditLogDays))
                    .ExecuteDeleteAsync(ct);
                var deliveries = await db.NotificationDeliveries
                    .Where(d => d.CreatedAtUtc < now.AddDays(-NotificationDeliveryDays))
                    .ExecuteDeleteAsync(ct);
                var lines = await db.Notifications
                    .Where(n => n.CreatedAtUtc < now.AddDays(-NotificationDays))
                    .ExecuteDeleteAsync(ct);
                var events = await db.NotificationEvents
                    .Where(e => (e.Status == Domain.Notifications.NotificationEventStatus.Done && e.CreatedAtUtc < now.AddDays(-NotificationEventDoneDays))
                                || (e.Status == Domain.Notifications.NotificationEventStatus.Failed && e.CreatedAtUtc < now.AddDays(-NotificationEventFailedDays)))
                    .ExecuteDeleteAsync(ct);

                // Backstop for the durable photo queue: the worker's retry budget (~5h) removes rows
                // itself, so anything a day old is unreachable garbage (e.g. rows orphaned by a bug).
                // Deleting it is a FAILURE being declared, not housekeeping — hence the error log.
                var stalePhotos = await db.PendingPhotoUploads
                    .Where(p => p.CreatedAtUtc < now.AddDays(-1))
                    .ExecuteDeleteAsync(ct);
                if (stalePhotos > 0)
                    _logger.LogError(
                        "DataRetentionJob: tenant {Tenant} dropped {Count} pending photo uploads older than 24h — these selfies are LOST",
                        tenantId, stalePhotos);

                if (scans + notifications + audits + deliveries + lines + events > 0)
                    _logger.LogInformation(
                        "DataRetentionJob: tenant {Tenant} pruned {Scans} processed scans, {Notifications} notifications, {Audits} audit rows, "
                        + "{Lines} center lines, {Deliveries} push deliveries, {Events} outbox events",
                        tenantId, scans, notifications, audits, lines, deliveries, events);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataRetentionJob: retention failed for tenant {Tenant}", tenantId);
            }
        }
    }

    // 01:30 local — after DailySummaryJob (00:30) and PhotoCleanupJob (01:00).
    private TimeSpan TimeUntilNextRun()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
        var todayRun = nowLocal.Date.AddMinutes(90); // 01:30 local
        var nextLocal = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), _timeZone);
        var delay = nextUtc - DateTime.UtcNow;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }
}
