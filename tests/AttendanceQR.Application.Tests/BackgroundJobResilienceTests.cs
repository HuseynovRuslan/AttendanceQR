using AttendanceQR.Api.DailyReport;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

    // ─────────────── DailySummaryJob: a failed run is tried again, not lost ───────────────
    // The finished-day readers (tabel, payroll) read nothing but DailySummaries, and a day without rows
    // reads as Qayıb for everyone. Before, a run that failed was logged and the job waited for the next
    // night — which wrote only ITS yesterday, putting the missed day behind the last summary, where the
    // catch-up (from the day after the last summary) can never reach it.

    private static readonly DateOnly Nov20 = new(2026, 11, 20);
    private static readonly DateOnly Nov21 = new(2026, 11, 21);
    private static readonly DateOnly Nov22 = new(2026, 11, 22);

    [Fact]
    public async Task Summary_night_whose_company_list_fails_is_tried_again_for_the_same_date()
    {
        var world = new SummaryWorld();
        var job = await world.StartedJobAsync();   // up and caught up on the 21st; now 00:30 on the 22nd

        world.DownForNext = 1;   // a Postgres restart, right at 00:30
        await job.RunNightAsync(Nov21, CancellationToken.None);

        Assert.Equal([(world.A, Nov21), (world.B, Nov21)], world.GeneratedInOrder());
        Assert.Contains(world.Log.Errors, e => e.Contains("could not be listed (attempt 1 of"));
        Assert.DoesNotContain(world.Log.Errors, e => e.Contains("gave up"));
    }

    [Fact]
    public async Task Summary_night_retries_only_the_company_that_failed()
    {
        var world = new SummaryWorld();
        var job = await world.StartedJobAsync();   // up and caught up on the 21st; now 00:30 on the 22nd

        var bFailures = 1;
        world.Fails = (tenant, _) => tenant == world.B && bFailures-- > 0;
        await job.RunNightAsync(Nov21, CancellationToken.None);

        // A once; B's failed attempt is not recorded, its retry is — and A is not regenerated for it.
        Assert.Equal([(world.A, Nov21), (world.B, Nov21)], world.GeneratedInOrder());
        Assert.Single(world.Generated, g => g.Tenant == world.A);
    }

    [Fact]
    public async Task Summary_startup_catch_up_whose_company_list_fails_is_tried_again()
    {
        var world = new SummaryWorld();
        world.Seed(world.A, Nov20);
        world.Seed(world.B, Nov20);
        world.Clock.Now = Baku(2026, 11, 23, 10, 0);   // the server was down for the 21st and 22nd
        var job = world.Job();

        world.DownForNext = 1;
        await job.CatchUpAsync(CancellationToken.None);

        Assert.Equal([(world.A, Nov21), (world.A, Nov22), (world.B, Nov21), (world.B, Nov22)], world.GeneratedInOrder());
    }

    [Fact]
    public async Task Summary_night_the_database_never_answered_is_caught_up_by_the_next_night()
    {
        var world = new SummaryWorld();
        world.Seed(world.A, Nov20);
        world.Seed(world.B, Nov20);
        var job = world.Job();
        await job.CatchUpAsync(CancellationToken.None);   // the 21st at noon: up to date, the 20th is stored
        Assert.Empty(world.Generated);

        // The night of the 21st: the database does not answer for any of the attempts.
        world.Clock.Now = Baku(2026, 11, 22, 0, 30);
        world.Down = true;
        await job.RunNightAsync(Nov21, CancellationToken.None);
        Assert.Empty(world.Generated);
        Assert.Equal(DailySummaryJob.MaxAttempts, world.Log.Errors.Count(e => e.Contains("could not be listed")));
        Assert.Contains(world.Log.Errors, e => e.Contains("gave up") && e.Contains("catches up"));

        // The next night writes the 22nd — and the 21st with it, rather than leaving it behind the 22nd.
        world.Clock.Now = Baku(2026, 11, 23, 0, 30);
        world.Down = false;
        await job.RunNightAsync(Nov22, CancellationToken.None);
        Assert.Equal([(world.A, Nov21), (world.A, Nov22), (world.B, Nov21), (world.B, Nov22)], world.GeneratedInOrder());

        // Caught up, the night after is an ordinary one again.
        world.Generated.Clear();
        world.Clock.Now = Baku(2026, 11, 24, 0, 30);
        await job.RunNightAsync(new DateOnly(2026, 11, 23), CancellationToken.None);
        Assert.Equal([(world.A, new DateOnly(2026, 11, 23)), (world.B, new DateOnly(2026, 11, 23))], world.GeneratedInOrder());
    }

    [Fact]
    public async Task Summary_company_that_fails_every_attempt_does_not_hold_back_its_later_nights()
    {
        // A failure the database answered around is the company's own (a bug, bad data): carrying it into
        // a catch-up would stop that company at the failing day every night after.
        var world = new SummaryWorld();
        var job = await world.StartedJobAsync();   // up and caught up on the 21st; now 00:30 on the 22nd

        world.Fails = (tenant, date) => tenant == world.B && date == Nov21;
        await job.RunNightAsync(Nov21, CancellationToken.None);
        Assert.Equal([(world.A, Nov21)], world.GeneratedInOrder());
        Assert.Contains(world.Log.Errors, e => e.Contains("gave up") && e.Contains("Günləri yenidən hesabla"));

        world.Clock.Now = Baku(2026, 11, 23, 0, 30);
        await job.RunNightAsync(Nov22, CancellationToken.None);
        Assert.Contains((world.B, Nov22), world.Generated);
    }

    [Fact]
    public async Task Summary_night_ends_at_once_on_shutdown_between_attempts()
    {
        var world = new SummaryWorld();
        var job = await world.StartedJobAsync(retryDelay: TimeSpan.FromMinutes(5));
        world.Down = true;
        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.RunNightAsync(Nov21, shutdown.Token));
        Assert.Single(world.Log.Errors, e => e.Contains("could not be listed"));
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

    private static DateTimeOffset Baku(int y, int mo, int d, int h, int mi) =>
        new(y, mo, d, h, mi, 0, TimeSpan.FromHours(4));

    /// <summary>
    /// Two companies on an in-memory database that can be switched off, and a summary service that
    /// records what it generated and stores a row for it — so "the day after the last summary" moves
    /// the way it does in production.
    /// </summary>
    private sealed class SummaryWorld
    {
        private readonly DbContextOptions<AppDbContext> _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"summary-job-{Guid.NewGuid()}", new InMemoryDatabaseRoot())
            .Options;

        public Guid A { get; } = Guid.NewGuid();
        public Guid B { get; } = Guid.NewGuid();
        public MovableClock Clock { get; } = new(Baku(2026, 11, 21, 12, 0));
        public ListLogger<DailySummaryJob> Log { get; } = new();
        public List<(Guid Tenant, DateOnly Date)> Generated { get; } = [];
        public IServiceScopeFactory Services { get; }

        /// <summary>No database context can be opened while set.</summary>
        public bool Down { get; set; }

        /// <summary>The next this-many contexts cannot be opened.</summary>
        public int DownForNext { get; set; }

        public Func<Guid, DateOnly, bool> Fails { get; set; } = (_, _) => false;

        public SummaryWorld()
        {
            using (var db = new AppDbContext(_options))
            {
                db.Tenants.Add(new Tenant { Id = A, Name = "A", Slug = "a", DisplayName = "A" });
                db.Tenants.Add(new Tenant { Id = B, Name = "B", Slug = "b", DisplayName = "B" });
                db.SaveChanges();
            }

            var services = new ServiceCollection();
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped(sp =>
            {
                if (Down)
                    throw new InvalidOperationException("the database is not answering");
                if (DownForNext > 0)
                {
                    DownForNext--;
                    throw new InvalidOperationException("the database is not answering");
                }
                return new AppDbContext(_options, sp.GetRequiredService<ITenantContext>());
            });
            services.AddScoped<IDailySummaryService>(sp => new RecordingSummaries(
                this, sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<AppDbContext>()));
            Services = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        }

        public DailySummaryJob Job(TimeSpan? retryDelay = null) =>
            new(Services, new AppOptions(), Log) { RetryDelay = retryDelay ?? TimeSpan.Zero, Clock = Clock };

        /// <summary>As the host leaves it: started on the 21st at noon and caught up, then 00:30 on the 22nd.</summary>
        public async Task<DailySummaryJob> StartedJobAsync(TimeSpan? retryDelay = null)
        {
            var job = Job(retryDelay);
            await job.CatchUpAsync(CancellationToken.None);
            Assert.Equal([(A, Nov20), (B, Nov20)], GeneratedInOrder());
            Generated.Clear();
            Clock.Now = Baku(2026, 11, 22, 0, 30);
            return job;
        }

        public void Seed(Guid tenant, DateOnly date)
        {
            var context = new TenantContext();
            context.Resolve(tenant);
            using var db = new AppDbContext(_options, context);
            db.DailySummaries.Add(new DailySummary { TenantId = tenant, EmployeeId = Guid.NewGuid(), SummaryDate = date });
            db.SaveChanges();
        }

        public List<(Guid Tenant, DateOnly Date)> GeneratedInOrder() =>
            Generated.OrderBy(g => g.Tenant == A ? 0 : 1).ThenBy(g => g.Date).ToList();

        private sealed class RecordingSummaries(SummaryWorld world, ITenantContext tenant, AppDbContext db) : IDailySummaryService
        {
            public async Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
            {
                if (world.Fails(tenant.TenantId, date))
                    throw new InvalidOperationException($"generation failed for {date}");
                db.DailySummaries.Add(new DailySummary { EmployeeId = Guid.NewGuid(), SummaryDate = date });
                await db.SaveChangesAsync(ct);
                world.Generated.Add((tenant.TenantId, date));
                return 1;
            }
        }
    }

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

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
