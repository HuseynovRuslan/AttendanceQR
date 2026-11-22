using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The replay marker (ProcessedScan) is what turns a re-sent offline scan into «AlreadyRecorded». The
/// scan endpoint accepts a replay for as long as the scan's own timestamp is inside the offline trust
/// window (thirty days), so the nightly retention must not delete the marker before that. It deleted
/// it after seven: a queue replayed on day eight recorded the same check-in or check-out a second time.
/// </summary>
public class ProcessedScanRetentionTests
{
    private static readonly DateTime RetentionRunsAt = new(2026, 11, 22, 21, 30, 0, DateTimeKind.Utc);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static bool Expired(DateTime nowUtc, ProcessedScan marker) =>
        DataRetentionJob.ExpiredProcessedScans(nowUtc).Compile()(marker);

    [Fact]
    public void The_marker_of_the_oldest_replay_the_scan_still_accepts_is_kept()
    {
        // A scan taken a minute inside the window is still accepted, so its marker has to be there.
        var takenAt = RetentionRunsAt.AddHours(-AttendanceController.OfflineTrustWindowHours).AddMinutes(1);
        // Worst case for the marker: the phone's clock ran the ten minutes fast the scan allows, so the
        // marker was written ten minutes before the time the scan carries.
        var marker = new ProcessedScan { ProcessedAtUtc = takenAt.AddMinutes(-10) };

        Assert.False(Expired(RetentionRunsAt, marker),
            "a replay the scan endpoint would still accept must still find its marker");
    }

    [Fact]
    public async Task Markers_a_week_old_or_more_survive_and_only_those_past_every_replay_go()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"replay-markers-{Guid.NewGuid()}").Options,
            tenant);
        foreach (var days in new[] { 1, 8, 20, 29 })
            db.ProcessedScans.Add(Marker(RetentionRunsAt.AddDays(-days)));
        var pastEveryReplay = Marker(RetentionRunsAt - DataRetentionJob.ProcessedScanRetention - TimeSpan.FromMinutes(1));
        db.ProcessedScans.Add(pastEveryReplay);
        await db.SaveChangesAsync();

        var expired = await db.ProcessedScans.Where(DataRetentionJob.ExpiredProcessedScans(RetentionRunsAt)).ToListAsync();

        Assert.Equal([pastEveryReplay.Id], expired.Select(p => p.Id));
    }

    [Fact]
    public void Retention_follows_the_trust_window_rather_than_a_number_of_its_own()
    {
        var window = TimeSpan.FromHours(AttendanceController.OfflineTrustWindowHours);
        Assert.True(DataRetentionJob.ProcessedScanRetention > window);
        // ...and stays a margin, not a second policy: the table is still pruned.
        Assert.True(DataRetentionJob.ProcessedScanRetention <= window + TimeSpan.FromDays(14));
    }

    private static ProcessedScan Marker(DateTime processedAtUtc) => new()
    {
        TenantId = TenantId,
        ClientScanId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        ProcessedAtUtc = processedAtUtc,
    };
}
