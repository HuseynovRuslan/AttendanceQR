using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Infrastructure.Multitenancy;
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

    // A run that fails is tried again within minutes, not the next night. The finished-day readers — the
    // tabel, payroll, the HR stats — read nothing but DailySummaries, and a day with no rows reads as
    // Qayıb with zero hours for everyone. The catch-up cannot reach such a day once a later one is stored
    // (it starts from the day after a company's last summary), so a Postgres restart at 00:30 that was
    // only logged marked a whole company absent for a day, unless somebody noticed and rebuilt it by
    // hand. Seven attempts, the wait doubling from five minutes: a restart is retried within minutes, and
    // the 00:30 run keeps trying until 05:45 — still before the morning scans. Every attempt is safe to
    // repeat: generation upserts a date's rows, it never adds to them.
    internal const int MaxAttempts = 7;
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(5);
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    // Owed when a run gave up while the database still was not answering — the company list could not be
    // read. The next night's run is then the catch-up rather than yesterday alone: a run that wrote only
    // yesterday would put the missed day behind every company's last summary, out of the catch-up's reach
    // for good. Starts owed, because the first run after startup IS the catch-up — for the days the
    // server was down.
    //
    // Not owed again when the companies could be listed and one of them still failed every attempt: that
    // is the company, not the database. The catch-up stops at a company's first failing day, so carried
    // from night to night it would keep that company from ever getting its later days. Such a failure is
    // logged and left for «Günləri yenidən hesabla», as it always was.
    private bool _catchUpOwed = true;

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
        // Only a shutdown leaves this method: every run catches its own failures (RunUntilDoneAsync). An
        // exception out of ExecuteAsync used to end the job for good, and under the host's default it
        // stopped the whole API with it.
        try
        {
            // Catch up on any days missed while the server was down.
            await CatchUpAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeUntilNextRun();
                _logger.LogInformation("DailySummaryJob: next run in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                await RunNightAsync(Yesterday(), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
    }

    /// <summary>
    /// One night's run, retries included: <paramref name="date"/> for every company — or, while one is
    /// owed, the catch-up, which reaches yesterday as well.
    /// </summary>
    internal Task RunNightAsync(DateOnly date, CancellationToken ct) =>
        _catchUpOwed ? CatchUpAsync(ct) : GenerateNightAsync(date, ct);

    /// <summary>The catch-up, retries included — what the job runs at startup.</summary>
    internal async Task CatchUpAsync(CancellationToken ct)
    {
        // A retry is the catch-up again: each company starts from the day after its last stored summary,
        // so one that went through is already at yesterday and is skipped, and one that failed resumes at
        // the day it failed on.
        var outcome = await RunUntilDoneAsync("backfill", async c => await BackfillAsync(c) == 0, ct);
        _catchUpOwed = outcome == Outcome.DatabaseNotAnswering;
    }

    private async Task GenerateNightAsync(DateOnly date, CancellationToken ct)
    {
        // A retry redoes the companies that failed, for the same date — never a recomputed "yesterday".
        // Null until the companies have been listed once: until then it is every one of them.
        IReadOnlySet<Guid>? pending = null;
        var outcome = await RunUntilDoneAsync($"the run for {date:yyyy-MM-dd}", async c =>
        {
            pending = await GenerateAsync(date, pending, c);
            return pending.Count == 0;
        }, ct);
        if (outcome == Outcome.DatabaseNotAnswering)
            _catchUpOwed = true;
    }

    private enum Outcome { Done, CompaniesFailed, DatabaseNotAnswering }

    // Runs `attempt` until it reports every company done — MaxAttempts at most, waiting RetryDelay, then
    // twice that, and so on. An exception out of an attempt is the company list failing (it is read
    // outside every per-company try), which is what a database that is not answering looks like. Only a
    // shutdown gets out of here.
    private async Task<Outcome> RunUntilDoneAsync(string what, Func<CancellationToken, Task<bool>> attempt, CancellationToken ct)
    {
        for (var n = 1; ; n++)
        {
            Outcome outcome;
            try
            {
                outcome = await attempt(ct) ? Outcome.Done : Outcome.CompaniesFailed;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailySummaryJob: {What} failed, the companies could not be listed (attempt {Attempt} of {Max})",
                    what, n, MaxAttempts);
                outcome = Outcome.DatabaseNotAnswering;
            }

            if (outcome == Outcome.Done)
            {
                if (n > 1)
                    _logger.LogInformation("DailySummaryJob: {What} done on attempt {Attempt}", what, n);
                return outcome;
            }
            if (n >= MaxAttempts)
            {
                if (outcome == Outcome.DatabaseNotAnswering)
                    _logger.LogError("DailySummaryJob: {What} gave up after {Attempts} attempts; the next night's run catches up", what, n);
                else
                    _logger.LogError("DailySummaryJob: {What} gave up after {Attempts} attempts; what failed is left for «Günləri yenidən hesabla»", what, n);
                return outcome;
            }
            var wait = RetryDelay * Math.Pow(2, n - 1);
            _logger.LogWarning("DailySummaryJob: {What} not done (attempt {Attempt} of {Max}); trying again in {Delay}",
                what, n, MaxAttempts, wait);
            await Task.Delay(wait, ct);
        }
    }

    // Runs yesterday because by 00:30 the previous day's check-outs are all in.
    private DateOnly Yesterday()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(Clock.GetUtcNow().UtcDateTime, _timeZone);
        return DateOnly.FromDateTime(nowLocal.Date.AddDays(-1));
    }

    private TimeSpan TimeUntilNextRun()
    {
        var nowUtc = Clock.GetUtcNow().UtcDateTime;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);
        var todayRun = nowLocal.Date.AddMinutes(30); // 00:30 local
        var nextLocal = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(nextLocal, DateTimeKind.Unspecified), _timeZone);
        var delay = nextUtc - nowUtc;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    // The summary service queries/writes through the tenant query filter, so it must run once PER
    // tenant with that tenant resolved — otherwise it would only ever process the default tenant.
    private async Task<List<Guid>> ActiveTenantIdsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
    }

    // Simple gap-fill per tenant: from the day after that tenant's last stored summary up to yesterday,
    // capped so a long outage can't spin for years. If nothing is stored yet, just do yesterday.
    // Returns how many tenants failed. A tenant stops at its first failing day, so its last summary is
    // exactly where a retry has to start.
    private async Task<int> BackfillAsync(CancellationToken ct)
    {
        var yesterday = Yesterday();
        var failed = 0;
        foreach (var tenantId in await ActiveTenantIdsAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var summaries = scope.ServiceProvider.GetRequiredService<IDailySummaryService>();

                var lastDate = await db.DailySummaries
                    .OrderByDescending(s => s.SummaryDate)
                    .Select(s => (DateOnly?)s.SummaryDate)
                    .FirstOrDefaultAsync(ct);

                var start = lastDate is DateOnly d ? d.AddDays(1) : yesterday;
                if (start > yesterday)
                    continue;
                if (yesterday.DayNumber - start.DayNumber > BackfillCapDays)
                    start = yesterday.AddDays(-BackfillCapDays);

                for (var day = start; day <= yesterday; day = day.AddDays(1))
                    await summaries.GenerateForDateAsync(day, ct);

                _logger.LogInformation("DailySummaryJob: backfilled {From}..{To} for tenant {Tenant}", start, yesterday, tenantId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex, "DailySummaryJob: backfill failed for tenant {Tenant}", tenantId);
            }
        }
        return failed;
    }

    // `only` narrows a retry to the tenants that failed before; null is every active tenant. Returns the
    // tenants that failed this time.
    private async Task<IReadOnlySet<Guid>> GenerateAsync(DateOnly date, IReadOnlySet<Guid>? only, CancellationToken ct)
    {
        var failed = new HashSet<Guid>();
        foreach (var tenantId in await ActiveTenantIdsAsync(ct))
        {
            if (only is not null && !only.Contains(tenantId))
                continue;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var summaries = scope.ServiceProvider.GetRequiredService<IDailySummaryService>();
                var count = await summaries.GenerateForDateAsync(date, ct);
                _logger.LogInformation("DailySummaryJob: generated {Count} summaries for {Date} tenant {Tenant}", count, date, tenantId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add(tenantId);
                _logger.LogError(ex, "DailySummaryJob: generation failed for {Date} tenant {Tenant}", date, tenantId);
            }
        }
        return failed;
    }
}
