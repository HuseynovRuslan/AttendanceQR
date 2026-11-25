using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// "Ayın işçisi" — a secret ballot held inside each branch over the last days of the month.
///
/// Design decisions worth keeping:
///  • Per BRANCH, because voting for someone you've never met is noise, and the biggest branch would
///    otherwise win every time.
///  • Everyone at the branch votes, managers included (the owner's call). Candidates stay employees —
///    it is the employee of the month, not the boss of the month.
///  • Candidates are shown as names only. Attendance figures used to sit under each name; they turned
///    the ballot into a scoreboard and made the choice look pre-decided, so the owner removed them.
///  • Truly anonymous: the ballot row and the tally row are separate, so no row anywhere links a
///    voter to a candidate (see MonthlyVoteBallot).
/// </summary>
[ApiController]
[Authorize]
[Route("api/vote")]
public class VoteController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;
    public VoteController(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    private DateTime NowLocal() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
    private DateOnly TodayLocal() => DateOnly.FromDateTime(NowLocal());
    private static DateOnly PeriodOf(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Everything the voting screen needs: whether it's open, whether I already voted, and my
    /// branch colleagues with this month's attendance behind each name.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var ct = HttpContext.RequestAborted;
        var employeeId = User.EmployeeId();
        var me = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (me is null)
            return Unauthorized(new { error = "EmployeeNotFound" });

        var today = TodayLocal();
        var period = PeriodOf(today);
        // No campaign for this month means the company chose not to run the award — not a
        // misconfiguration, and the screen says exactly that.
        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == period, ct);
        var open = campaign?.IsOpenAt(NowLocal()) ?? false;

        var colleagues = await _db.Employees
            .Where(e => e.IsActive && e.LocationId == me.LocationId && e.Id != me.Id && e.Role == EmployeeRole.Employee)
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);

        // Positions the campaign bars from being nominated. Filtered in memory: Position is nullable
        // free text, and someone with no position set stays a candidate.
        if (campaign is { ExcludedPositions.Count: > 0 })
            colleagues = colleagues.Where(e => !campaign.ExcludedPositions.Contains(e.Position ?? "")).ToList();

        var hasVoted = await _db.MonthlyVoteBallots
            .AnyAsync(b => b.Period == period && b.VoterEmployeeId == employeeId, ct);

        var locationName = await _db.Locations
            .Where(l => l.Id == me.LocationId).Select(l => l.Name).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            isOpen = open,
            opensOn = campaign?.StartsOn,
            closesOn = campaign?.EndsOn,
            opensAt = campaign?.StartsAt.ToString("HH:mm"),
            closesAt = campaign?.EndsAt.ToString("HH:mm"),
            hasVoted,
            // Everyone at the branch votes, managers included.
            enabled = campaign is not null,
            canVote = campaign is not null && colleagues.Count >= campaign.MinCandidates,
            tooFewColleagues = campaign is not null && colleagues.Count < campaign.MinCandidates,
            locationName,
            period,
            candidates = colleagues.Select(c => new
            {
                employeeId = c.Id,
                fullName = c.FullName,
                position = c.Position,
            }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Cast([FromBody] VoteRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var employeeId = User.EmployeeId();
        var me = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
        if (me is null)
            return Unauthorized(new { error = "EmployeeNotFound" });
        var today = TodayLocal();
        var period = PeriodOf(today);
        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == period, ct);
        if (campaign is null || !campaign.IsOpenAt(NowLocal()))
            return BadRequest(new { error = "VotingClosed" });

        if (request.CandidateEmployeeId == employeeId)
            return BadRequest(new { error = "CannotVoteForSelf" });

        var candidate = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == request.CandidateEmployeeId && e.IsActive && e.LocationId == me.LocationId, ct);
        if (candidate is null)
            return BadRequest(new { error = "CandidateNotInYourBranch" });
        // Re-checked here, not just hidden from the list: a stale screen must not be able to nominate
        // someone the campaign excluded.
        if (campaign.ExcludedPositions.Contains(candidate.Position ?? ""))
            return BadRequest(new { error = "CandidateNotEligible" });

        // The ballot and its count are ONE write. They used to be two saves: the ballot committed on its
        // own, then the tally was read, bumped and saved. Two colleagues voting for the same person in the
        // same moment — the opening push brings a whole branch in at once — both read "no tally yet", both
        // inserted one, and the second hit the unique index with a 500 AFTER its ballot was already in: the
        // vote was never counted and the retry said «Bu ay artıq səs vermisiniz». With a tally row already
        // there, the two read-modify-writes wrote the same N+1 and one vote vanished without any error.
        // Now a failure anywhere rolls the ballot back too, and the count is one atomic statement.
        // (In-memory tests have no transactions; there the two steps simply run in order.)
        await using var tx = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;

        // The ballot goes in FIRST: its unique (period, voter) index is what makes the vote single-use,
        // and if it loses that race nothing is tallied.
        var ballot = _db.MonthlyVoteBallots.Add(new MonthlyVoteBallot { Period = period, VoterEmployeeId = employeeId });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            ballot.State = EntityState.Detached;
            return Conflict(new { error = "AlreadyVoted" });
        }

        await CountVoteAsync(period, me.LocationId, candidate.Id, ct);
        if (tx is not null)
            await tx.CommitAsync(ct);

        return Ok(new { ok = true });
    }

    /// <summary>
    /// One more vote for the candidate. On Postgres a single INSERT … ON CONFLICT … DO UPDATE: the first vote
    /// creates the row, every later one adds 1 to whatever the row holds at that instant, and two at once
    /// queue on the row lock instead of overwriting each other. The branch is the voter's — the candidate's
    /// own, since a candidate must be from the voter's branch — and is set when the row is created.
    /// The in-memory provider runs no SQL, so the tests take the plain read-and-add path.
    /// </summary>
    private async Task CountVoteAsync(DateOnly period, Guid locationId, Guid candidateId, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            // TenantId is written explicitly: raw SQL bypasses the save-time stamping. CurrentTenantId throws when
            // no company is resolved, so this can never count a vote under the wrong one.
            await _db.Database.ExecuteSqlInterpolatedAsync(TallyUpsert(_db.CurrentTenantId, period, locationId, candidateId), ct);
            return;
        }

        var tally = await _db.MonthlyVoteTallies
            .FirstOrDefaultAsync(t => t.Period == period && t.CandidateEmployeeId == candidateId, ct);
        if (tally is null)
            _db.MonthlyVoteTallies.Add(new MonthlyVoteTally
            {
                Period = period,
                LocationId = locationId,
                CandidateEmployeeId = candidateId,
                Votes = 1,
            });
        else
            tally.Votes++;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>The count as one statement. Its conflict target must be exactly the tally's unique index, or
    /// Postgres refuses every vote — VoteTallyTests holds it to the model.</summary>
    internal static FormattableString TallyUpsert(Guid tenantId, DateOnly period, Guid locationId, Guid candidateId) => $"""
        INSERT INTO "MonthlyVoteTallies" ("Id", "TenantId", "Period", "LocationId", "CandidateEmployeeId", "Votes")
        VALUES ({Guid.NewGuid()}, {tenantId}, {period}, {locationId}, {candidateId}, 1)
        ON CONFLICT ("TenantId", "Period", "CandidateEmployeeId")
        DO UPDATE SET "Votes" = "MonthlyVoteTallies"."Votes" + 1
        """;

    /// <summary>Results. While voting is open only the total turnout is returned — publishing a running
    /// scoreboard turns the last day into a bandwagon.</summary>
    [HttpGet("results")]
    public async Task<IActionResult> Results([FromQuery] DateOnly? period)
    {
        var ct = HttpContext.RequestAborted;
        var today = TodayLocal();
        var p = period is null ? PeriodOf(today) : PeriodOf(period.Value);
        var isCurrent = p == PeriodOf(today);
        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == p, ct);
        var open = campaign?.IsOpenAt(NowLocal()) ?? false;
        // Employees see no running scoreboard while the ballot is open; whoever runs the vote does,
        // because they need to watch turnout and chase the branches that haven't voted.
        var organiser = User.Role() is EmployeeRole.Admin or EmployeeRole.Manager;
        var hidden = isCurrent && open && !organiser;

        var tallies = await _db.MonthlyVoteTallies.Where(t => t.Period == p).ToListAsync(ct);
        var castCount = await _db.MonthlyVoteBallots.CountAsync(b => b.Period == p, ct);

        if (hidden)
            return Ok(new { period = p, open = true, votesCast = castCount, branches = Array.Empty<object>() });

        var names = await _db.Employees
            .Where(e => tallies.Select(t => t.CandidateEmployeeId).Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var locations = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        var branches = tallies
            .GroupBy(t => t.LocationId)
            .Select(g => new
            {
                locationId = g.Key,
                locationName = locations.GetValueOrDefault(g.Key, ""),
                results = g.OrderByDescending(t => t.Votes)
                    .Select(t => new { employeeId = t.CandidateEmployeeId, fullName = names.GetValueOrDefault(t.CandidateEmployeeId, "—"), votes = t.Votes }),
            })
            .OrderBy(b => b.locationName);

        var winners = await _db.MonthlyWinners
            .Where(w => w.Period == p)
            .Select(w => new { w.LocationId, w.EmployeeId, w.Votes })
            .ToListAsync(ct);

        return Ok(new { period = p, open = isCurrent && open, votesCast = castCount, branches, winners });
    }

    /// <summary>The caller's own wins — powers the 🏆 badge on their home screen.</summary>
    [HttpGet("my-awards")]
    public async Task<IActionResult> MyAwards()
    {
        var employeeId = User.EmployeeId();
        var rows = await _db.MonthlyWinners
            .Where(w => w.EmployeeId == employeeId)
            .OrderByDescending(w => w.Period)
            .Select(w => new { period = w.Period, votes = w.Votes })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }
}
