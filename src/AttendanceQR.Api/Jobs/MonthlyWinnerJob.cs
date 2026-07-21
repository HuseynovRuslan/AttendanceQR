using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Closes the "Ayın işçisi" ballot: once a month is over, decides each branch's winner, records it,
/// and announces the result to everyone (in-app banner + push).
///
/// Idempotent by construction — the winner row's unique (period, location) index means a second pass
/// simply loses the insert, so an hourly sweep can never announce twice. That is also why this is a
/// sweep rather than a precisely-timed job: if the server is down at midnight on the 1st, the result
/// is still published as soon as it comes back.
///
/// Ties break on attendance: with equal votes the one who actually turned up more days wins.
/// </summary>
public sealed class MonthlyWinnerJob : BackgroundService
{
    // Five minutes, not an hour: campaigns now open at a chosen time of day, and "voting is open" is
    // stale news if it lands 50 minutes into the window. Deciding a winner stays idempotent either way.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<MonthlyWinnerJob> _logger;

    public MonthlyWinnerJob(
        IServiceScopeFactory scopeFactory, AppOptions appOptions, ILogger<MonthlyWinnerJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyWinnerJob: sweep failed");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<List<Guid>> ActiveTenantIdsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        foreach (var tenantId in await ActiveTenantIdsAsync(ct))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<IPushNotifier>();

                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
                var todayLocal = DateOnly.FromDateTime(nowLocal);
                var thisPeriod = new DateOnly(todayLocal.Year, todayLocal.Month, 1);
                var lastPeriod = thisPeriod.AddMonths(-1);

                // A ballot nobody is told about gets a handful of votes. The notice goes out on the
                // opening day only — OpenedNotifiedAtUtc makes it once per campaign, not once an hour.
                var thisCampaign = await db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == thisPeriod, ct);
                if (thisCampaign is not null)
                {
                    var announcer = scope.ServiceProvider.GetRequiredService<IVoteAnnouncer>();
                    if (await announcer.AnnounceOpeningAsync(thisCampaign, nowLocal, ct))
                        _logger.LogInformation("MonthlyWinnerJob: announced ballot opening for {Period}", thisPeriod);
                }

                // Only months someone actually ran a ballot for get a winner. Stray tallies from a
                // campaign that was later deleted must never surface as a company-wide announcement.
                var campaign = await db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == lastPeriod, ct);
                if (campaign is null) continue;

                var tallies = await db.MonthlyVoteTallies.Where(t => t.Period == lastPeriod).ToListAsync(ct);
                if (tallies.Count == 0) continue;

                var alreadyDecided = await db.MonthlyWinners
                    .Where(w => w.Period == lastPeriod).Select(w => w.LocationId).ToListAsync(ct);
                var pending = tallies
                    .GroupBy(t => t.LocationId)
                    .Where(g => !alreadyDecided.Contains(g.Key))
                    // Too few votes to mean anything — announcing a winner chosen by a handful of
                    // people devalues the award, so that branch simply gets no winner this month.
                    .Where(g => g.Sum(t => t.Votes) >= campaign.MinVotesToDecide)
                    .ToList();
                if (pending.Count == 0) continue;

                // Tie-break data: days actually worked in the month being decided.
                var candidateIds = tallies.Select(t => t.CandidateEmployeeId).Distinct().ToList();
                var monthEnd = lastPeriod.AddMonths(1);
                var attended = (await db.AttendanceRecords
                        .Where(r => candidateIds.Contains(r.EmployeeId) && r.AttendanceDate >= lastPeriod
                                    && r.AttendanceDate < monthEnd && r.CheckInAtUtc != null)
                        .Select(r => new { r.EmployeeId, r.AttendanceDate })
                        .ToListAsync(ct))
                    .GroupBy(x => x.EmployeeId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.AttendanceDate).Distinct().Count());

                var names = await db.Employees
                    .Where(e => candidateIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
                var locations = await db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);

                var decided = new List<(string Location, string Name, int Votes)>();
                foreach (var group in pending)
                {
                    var best = group
                        .OrderByDescending(t => t.Votes)
                        .ThenByDescending(t => attended.GetValueOrDefault(t.CandidateEmployeeId, 0))
                        .First();

                    db.MonthlyWinners.Add(new MonthlyWinner
                    {
                        Period = lastPeriod,
                        LocationId = group.Key,
                        EmployeeId = best.CandidateEmployeeId,
                        Votes = best.Votes,
                    });
                    decided.Add((
                        locations.GetValueOrDefault(group.Key, ""),
                        names.GetValueOrDefault(best.CandidateEmployeeId, "—"),
                        best.Votes));
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Another instance decided it first — nothing to announce.
                    continue;
                }

                // Announce: the same channel employees already watch (home banner + notifications tab),
                // and a push so it actually reaches them.
                var monthName = AzMonth(lastPeriod.Month);
                var lines = decided.Select(d => $"{d.Location}: {d.Name}").ToList();
                var message = $"{monthName} ayının işçiləri seçildi 🏆\n" + string.Join("\n", lines) +
                              "\n\nSəs verən hər kəsə təşəkkür edirik!";

                db.Announcements.Add(new Announcement
                {
                    Title = $"{monthName} ayının işçisi",
                    Message = message,
                    Audience = AnnouncementAudience.All,
                });
                await db.SaveChangesAsync(ct);

                var everyone = await db.Employees.Where(e => e.IsActive).Select(e => e.Id).ToListAsync(ct);
                await notifier.NotifyEmployeesAsync(
                    everyone, $"{monthName} ayının işçisi 🏆", string.Join(" · ", lines), "/home", ct);

                _logger.LogInformation(
                    "MonthlyWinnerJob: tenant {Tenant} decided {Count} winners for {Period}", tenantId, decided.Count, lastPeriod);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyWinnerJob: tenant {Tenant} failed", tenantId);
            }
        }
    }

    private static string AzMonth(int m) => m switch
    {
        1 => "Yanvar", 2 => "Fevral", 3 => "Mart", 4 => "Aprel", 5 => "May", 6 => "İyun",
        7 => "İyul", 8 => "Avqust", 9 => "Sentyabr", 10 => "Oktyabr", 11 => "Noyabr", _ => "Dekabr",
    };
}
