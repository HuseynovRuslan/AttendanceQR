using System.Linq.Expressions;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Nightly retention for the three ledger tables every scan writes to and nothing ever pruned:
/// ProcessedScans, EmployeeNotifications, AuditLogs. At 2000 employees they grow ~5M rows/year
/// combined; their useful lives are the offline trust window (thirty days), ~a season, and ~a year
/// respectively. Same plain BackgroundService + local-timezone timer pattern as
/// <see cref="DailySummaryJob"/>; runs at 01:30 local, after the 00:30 summaries and the 01:00 photo
/// cleanup.
///
/// AttendanceRecords and DailySummaries are deliberately NOT here — they are the attendance ledger
/// itself (pay disputes, tabel history) and are never aged out.
/// </summary>
public sealed class DataRetentionJob : BackgroundService
{
    // ProcessedScans exist only to deduplicate replayed offline scans, so a marker has to outlive every
    // replay the scan endpoint still accepts — and it accepts one for as long as the scan's own
    // timestamp is inside AttendanceController.OfflineTrustWindowHours. This was a fixed seven days,
    // written when that window was 18 hours; the window grew to thirty days and this did not, so a
    // phone replaying its queue after day seven had the same check-in or check-out recorded twice.
    // Derived from the constant now, so the two cannot drift apart again. The week on top covers a
    // phone clock running fast (the scan allows ten minutes) many times over; at ~114 people that is a
    // few thousand rows per company, on an index the delete already uses.
    internal static readonly TimeSpan ProcessedScanRetention =
        TimeSpan.FromHours(AttendanceController.OfflineTrustWindowHours) + TimeSpan.FromDays(7);

    /// <summary>The markers no replay can need any more. One predicate for the delete and its test.</summary>
    internal static Expression<Func<ProcessedScan, bool>> ExpiredProcessedScans(DateTime nowUtc)
    {
        var cutoff = nowUtc - ProcessedScanRetention;
        return p => p.ProcessedAtUtc < cutoff;
    }

    // Reminders/nudges an employee has long since seen (the bell shows a handful of recent ones).
    private const int EmployeeNotificationDays = 90;

    // Rejected-scan forensics (the problems screen reads 7 days; disputes reach back months). A year
    // covers any argument that ends up mattering, and keeps the table at a bounded steady state.
    private const int AuditLogDays = 365;

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

            // Backstop for whatever RunOnceAsync does not keep in itself: an exception out of
            // ExecuteAsync ends the job for good, and under the host's default it stopped the API too.
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataRetentionJob: run failed; the next night tries again");
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Per tenant, like every other job: the query filters scope each delete to the resolved
        // tenant, so this never needs (and never gets) IgnoreQueryFilters.
        //
        // The company list is read outside the per-tenant try, so it has its own: a database that is
        // not answering at 01:30 costs one night's pruning — nothing here is urgent — never the process.
        List<Guid> tenantIds;
        try
        {
            using var listScope = _scopeFactory.CreateScope();
            var db = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataRetentionJob: could not list the companies; skipping tonight's retention");
            return;
        }

        foreach (var tenantId in tenantIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var scans = await db.ProcessedScans
                    .Where(ExpiredProcessedScans(now))
                    .ExecuteDeleteAsync(ct);
                var notifications = await db.EmployeeNotifications
                    .Where(n => n.CreatedAtUtc < now.AddDays(-EmployeeNotificationDays))
                    .ExecuteDeleteAsync(ct);
                var audits = await db.AuditLogs
                    .Where(a => a.CreatedAtUtc < now.AddDays(-AuditLogDays))
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

                if (scans + notifications + audits > 0)
                    _logger.LogInformation(
                        "DataRetentionJob: tenant {Tenant} pruned {Scans} processed scans, {Notifications} notifications, {Audits} audit rows",
                        tenantId, scans, notifications, audits);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
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
