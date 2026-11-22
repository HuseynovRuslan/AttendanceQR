using AttendanceQR.Api.DailyReport;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A background job's tick that fails must stay inside that job. An exception out of ExecuteAsync ends
/// the job for good, and under the host's default (StopHost) it ended the whole API: one Postgres restart
/// at the 08:00 scan peak, read by a report job listing the companies, and nobody could clock in.
///
/// The database here is "down" in the bluntest way — the DbContext cannot even be created — which is
/// what the company-list read outside every per-tenant try ran into.
/// </summary>
public class BackgroundJobResilienceTests
{
    [Fact]
    public async Task Daily_report_tick_survives_a_database_that_is_down()
    {
        var log = new ListLogger<DailyReportJob>();
        var job = new DailyReportJob(DatabaseDown(), new DailyReportOptions(), log);

        await job.RunOnceAsync(CancellationToken.None);   // must not throw

        Assert.Contains(log.Errors, e => e.Contains("could not list the companies"));
    }

    [Fact]
    public async Task Data_retention_run_survives_a_database_that_is_down()
    {
        var log = new ListLogger<DataRetentionJob>();
        var job = new DataRetentionJob(DatabaseDown(), new AppOptions(), log);

        await job.RunOnceAsync(CancellationToken.None);   // must not throw

        Assert.Contains(log.Errors, e => e.Contains("could not list the companies"));
    }

    [Fact]
    public async Task Daily_summary_job_keeps_running_when_the_startup_backfill_cannot_reach_the_database()
    {
        // The backfill runs the moment the job starts, so this one goes through the real ExecuteAsync.
        var log = new ListLogger<DailySummaryJob>();
        var job = new DailySummaryJob(DatabaseDown(), new AppOptions(), log);

        await job.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!job.ExecuteTask!.IsCompleted && !log.Errors.Any(e => e.Contains("backfill failed"))
                   && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            await Task.Delay(100);   // give a would-be fault the moment to land

            // Still alive, waiting for 00:30 — not a faulted task the host would act on.
            Assert.False(job.ExecuteTask!.IsCompleted, $"the job ended: {job.ExecuteTask.Exception?.GetBaseException().Message}");
            Assert.Contains(log.Errors, e => e.Contains("backfill failed"));
        }
        finally
        {
            await job.StopAsync(CancellationToken.None);
        }
        Assert.False(job.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Summary_rebuild_survives_a_cancellation_that_is_not_a_shutdown()
    {
        // A timeout surfacing as TaskCanceledException is a failure like any other; it used to be let
        // through as if it were the shutdown, out of ExecuteAsync.
        var log = new ListLogger<SummaryRebuildWorker>();
        var worker = new SummaryRebuildWorker(new SummaryRebuildQueue(), Services(new ThrowingSummaries(new TaskCanceledException("timed out"))), log);

        await worker.RebuildAsync(Guid.NewGuid(), new DateOnly(2026, 11, 20), CancellationToken.None);   // must not throw

        Assert.Contains(log.Errors, e => e.Contains("failed for tenant"));
    }

    [Fact]
    public async Task Summary_rebuild_still_ends_on_a_real_shutdown()
    {
        var log = new ListLogger<SummaryRebuildWorker>();
        var worker = new SummaryRebuildWorker(new SummaryRebuildQueue(), Services(new ThrowingSummaries(new OperationCanceledException())), log);
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.RebuildAsync(Guid.NewGuid(), new DateOnly(2026, 11, 20), shutdown.Token));
        Assert.Empty(log.Errors);
    }

    // ───────────────────────────── fixtures ─────────────────────────────

    private static IServiceScopeFactory DatabaseDown()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<AppDbContext>(_ => throw new InvalidOperationException("the database is not answering"));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static IServiceScopeFactory Services(IDailySummaryService summaries)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton(summaries);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class ThrowingSummaries(Exception error) : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => throw error;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Errors
        {
            get { lock (_errors) return _errors.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error) return;
            lock (_errors) _errors.Add(formatter(state, exception));
        }
    }
}
