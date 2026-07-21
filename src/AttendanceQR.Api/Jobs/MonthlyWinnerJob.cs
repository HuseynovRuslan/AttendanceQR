using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Jobs;

/// <summary>
/// Closes the "Ayın işçisi" ballot: once a campaign's window has passed, decides each branch's winner,
/// records it, congratulates the winners personally and announces the result to everyone.
///
/// Keyed on the campaign closing, not on the month rolling over. A ballot that ends at 18:00 on the
/// 31st used to wait until the 1st for its result — and one that ran mid-month waited a fortnight.
/// The moment people vote is the moment they want to know.
///
/// Idempotent by construction — the winner row's unique (period, location) index means a second pass
/// simply loses the insert, so a repeated sweep can never announce twice. That is also why this is a
/// sweep rather than a precisely-timed job: if the server is down when a ballot closes, the result
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

                // Every ballot whose window has passed. Two periods is enough: a closed campaign is
                // decided within one sweep, and looking further back would only re-scan settled months.
                var closed = (await db.VoteCampaigns
                        .Where(c => c.Period == thisPeriod || c.Period == lastPeriod)
                        .ToListAsync(ct))
                    .Where(c => nowLocal > c.ClosesAtLocal);

                foreach (var closedCampaign in closed)
                    await DecideAsync(db, notifier, closedCampaign, tenantId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyWinnerJob: tenant {Tenant} failed", tenantId);
            }
        }
    }

    /// <summary>Decides and announces one campaign's winners. Does nothing once they are settled.</summary>
    private async Task DecideAsync(
        AppDbContext db, IPushNotifier notifier, VoteCampaign campaign, Guid tenantId, CancellationToken ct)
    {
        var period = campaign.Period;
        var tallies = await db.MonthlyVoteTallies.Where(t => t.Period == period).ToListAsync(ct);
        if (tallies.Count == 0) return;

        var alreadyDecided = await db.MonthlyWinners
            .Where(w => w.Period == period).Select(w => w.LocationId).ToListAsync(ct);
        var pending = tallies
            .GroupBy(t => t.LocationId)
            .Where(g => !alreadyDecided.Contains(g.Key))
            // Too few votes to mean anything — announcing a winner chosen by a handful of people
            // devalues the award, so that branch simply gets no winner this month.
            .Where(g => g.Sum(t => t.Votes) >= campaign.MinVotesToDecide)
            .ToList();
        if (pending.Count == 0) return;

        // Tie-break data: days actually worked in the month being decided.
        var candidateIds = tallies.Select(t => t.CandidateEmployeeId).Distinct().ToList();
        var monthEnd = period.AddMonths(1);
        var attended = (await db.AttendanceRecords
                .Where(r => candidateIds.Contains(r.EmployeeId) && r.AttendanceDate >= period
                            && r.AttendanceDate < monthEnd && r.CheckInAtUtc != null)
                .Select(r => new { r.EmployeeId, r.AttendanceDate })
                .ToListAsync(ct))
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AttendanceDate).Distinct().Count());

        var names = await db.Employees
            .Where(e => candidateIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var locations = await db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        var decided = new List<(Guid EmployeeId, string Location, string Name, int Votes)>();
        foreach (var group in pending)
        {
            var best = group
                .OrderByDescending(t => t.Votes)
                .ThenByDescending(t => attended.GetValueOrDefault(t.CandidateEmployeeId, 0))
                .First();

            db.MonthlyWinners.Add(new MonthlyWinner
            {
                Period = period,
                LocationId = group.Key,
                EmployeeId = best.CandidateEmployeeId,
                Votes = best.Votes,
            });
            decided.Add((
                best.CandidateEmployeeId,
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
            return;
        }

        var monthName = AzMonth(period.Month);
        var lines = decided.Select(d => $"{d.Location}: {d.Name}").ToList();

        // Announce: the same channel employees already watch (home banner + notifications tab), and a
        // push so it actually reaches them.
        db.Announcements.Add(new Announcement
        {
            Title = $"{monthName} ayının işçisi 🏆",
            Message = $"{monthName} ayının işçiləri seçildi 🏆\n" + string.Join("\n", lines) +
                      "\n\nSəs verən hər kəsə təşəkkür edirik!",
            Audience = AnnouncementAudience.All,
        });
        await db.SaveChangesAsync(ct);

        var everyone = await db.Employees.Where(e => e.IsActive).Select(e => e.Id).ToListAsync(ct);
        await notifier.NotifyEmployeesAsync(
            everyone, $"{monthName} ayının işçisi 🏆", string.Join(" · ", lines), "/home", ct);

        // And a word to each winner personally. Reading your own name in a company-wide list is not
        // the same as being congratulated, and this award is worth nothing if it doesn't feel personal.
        foreach (var winner in decided)
            await notifier.NotifyEmployeesAsync(
                new[] { winner.EmployeeId },
                "Təbrik edirik! 🏆",
                $"{monthName} ayının işçisi seçildiniz. Komandanız sizə {winner.Votes} səs verdi.",
                "/home", ct);

        _logger.LogInformation(
            "MonthlyWinnerJob: tenant {Tenant} decided {Count} winners for {Period}", tenantId, decided.Count, period);
    }

    private static string AzMonth(int m) => m switch
    {
        1 => "Yanvar", 2 => "Fevral", 3 => "Mart", 4 => "Aprel", 5 => "May", 6 => "İyun",
        7 => "İyul", 8 => "Avqust", 9 => "Sentyabr", 10 => "Oktyabr", 11 => "Noyabr", _ => "Dekabr",
    };
}
