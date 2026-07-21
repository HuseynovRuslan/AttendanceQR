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
///  • Candidates are shown WITH their attendance figures, so it's a judgement about work rather than
///    a pure popularity contest.
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

    private DateOnly TodayLocal() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
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
        var open = campaign?.IsOpenOn(today) ?? false;

        var colleagues = await _db.Employees
            .Where(e => e.IsActive && e.LocationId == me.LocationId && e.Id != me.Id && e.Role == EmployeeRole.Employee)
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);

        var hasVoted = await _db.MonthlyVoteBallots
            .AnyAsync(b => b.Period == period && b.VoterEmployeeId == employeeId, ct);

        // This month's attendance behind each candidate, so the choice rests on something.
        var from = period;
        var ids = colleagues.Select(c => c.Id).ToList();
        var records = await _db.AttendanceRecords
            .Where(r => ids.Contains(r.EmployeeId) && r.AttendanceDate >= from && r.CheckInAtUtc != null)
            .Select(r => new { r.EmployeeId, r.AttendanceDate })
            .ToListAsync(ct);
        var daysByEmployee = records
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.AttendanceDate).Distinct().Count());

        var locationName = await _db.Locations
            .Where(l => l.Id == me.LocationId).Select(l => l.Name).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            isOpen = open,
            opensOn = campaign?.StartsOn,
            closesOn = campaign?.EndsOn,
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
                daysPresent = daysByEmployee.GetValueOrDefault(c.Id, 0),
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
        if (campaign is null || !campaign.IsOpenOn(today))
            return BadRequest(new { error = "VotingClosed" });

        if (request.CandidateEmployeeId == employeeId)
            return BadRequest(new { error = "CannotVoteForSelf" });

        var candidate = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == request.CandidateEmployeeId && e.IsActive && e.LocationId == me.LocationId, ct);
        if (candidate is null)
            return BadRequest(new { error = "CandidateNotInYourBranch" });

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

        var tally = await _db.MonthlyVoteTallies
            .FirstOrDefaultAsync(t => t.Period == period && t.CandidateEmployeeId == candidate.Id, ct);
        if (tally is null)
        {
            _db.MonthlyVoteTallies.Add(new MonthlyVoteTally
            {
                Period = period,
                LocationId = me.LocationId,
                CandidateEmployeeId = candidate.Id,
                Votes = 1,
            });
        }
        else
        {
            tally.Votes++;
        }
        await _db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

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
        var open = campaign?.IsOpenOn(today) ?? false;
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
