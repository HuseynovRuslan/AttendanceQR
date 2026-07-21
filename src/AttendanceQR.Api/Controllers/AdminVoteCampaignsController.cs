using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Creating and running the "Ayın işçisi" ballot. A month only has a vote because someone made one —
/// there is no automatic window, so a company that skips a month simply doesn't create a campaign.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/vote-campaigns")]
public class AdminVoteCampaignsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public AdminVoteCampaignsController(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    private DateOnly TodayLocal() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

    private object Project(VoteCampaign c, DateOnly today, int votes) => new
    {
        id = c.Id,
        period = c.Period,
        startsOn = c.StartsOn,
        endsOn = c.EndsOn,
        minCandidates = c.MinCandidates,
        minVotesToDecide = c.MinVotesToDecide,
        excludedPositions = c.ExcludedPositions,
        votesCast = votes,
        isOpen = c.IsOpenOn(today),
        // Three distinct states the admin needs to tell apart at a glance.
        state = c.IsOpenOn(today) ? "open" : today < c.StartsOn ? "scheduled" : "finished",
    };

    /// <summary>The campaign for a month (null when none was created — that month has no ballot).</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? period)
    {
        var ct = HttpContext.RequestAborted;
        var today = TodayLocal();
        var p = period is null ? new DateOnly(today.Year, today.Month, 1) : new DateOnly(period.Value.Year, period.Value.Month, 1);

        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == p, ct);
        if (campaign is null)
            return Ok(new { period = p, campaign = (object?)null });

        var votes = await _db.MonthlyVoteBallots.CountAsync(b => b.Period == p, ct);
        return Ok(new { period = p, campaign = Project(campaign, today, votes) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] VoteCampaignRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (Validate(request) is { } error)
            return BadRequest(new { error });

        var period = new DateOnly(request.StartsOn.Year, request.StartsOn.Month, 1);
        if (await _db.VoteCampaigns.AnyAsync(c => c.Period == period, ct))
            return Conflict(new { error = "CampaignAlreadyExists" });

        var campaign = new VoteCampaign
        {
            Period = period,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            MinCandidates = request.MinCandidates,
            MinVotesToDecide = request.MinVotesToDecide,
            ExcludedPositions = Clean(request.ExcludedPositions),
        };
        _db.VoteCampaigns.Add(campaign);
        await _db.SaveChangesAsync(ct);

        var today = TodayLocal();
        return Ok(new { period, campaign = Project(campaign, today, 0) });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] VoteCampaignRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (Validate(request) is { } error)
            return BadRequest(new { error });

        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null)
            return NotFound(new { error = "CampaignNotFound" });

        // The period is the identity of the ballot — votes are filed under it. Moving it would orphan
        // them, so the dates may shift only inside the same month.
        var period = new DateOnly(request.StartsOn.Year, request.StartsOn.Month, 1);
        if (period != campaign.Period)
            return BadRequest(new { error = "CannotMoveToAnotherMonth" });

        campaign.StartsOn = request.StartsOn;
        campaign.EndsOn = request.EndsOn;
        campaign.MinCandidates = request.MinCandidates;
        campaign.MinVotesToDecide = request.MinVotesToDecide;
        campaign.ExcludedPositions = Clean(request.ExcludedPositions);
        await _db.SaveChangesAsync(ct);

        var today = TodayLocal();
        var votes = await _db.MonthlyVoteBallots.CountAsync(b => b.Period == campaign.Period, ct);
        return Ok(new { period = campaign.Period, campaign = Project(campaign, today, votes) });
    }

    /// <summary>Deletes the ballot AND everything cast in it — the way to undo a trial run or a
    /// campaign created by mistake. Irreversible, so the UI names the month before calling this.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null)
            return NotFound(new { error = "CampaignNotFound" });

        var p = campaign.Period;
        _db.MonthlyVoteBallots.RemoveRange(await _db.MonthlyVoteBallots.Where(b => b.Period == p).ToListAsync(ct));
        _db.MonthlyVoteTallies.RemoveRange(await _db.MonthlyVoteTallies.Where(t => t.Period == p).ToListAsync(ct));
        _db.MonthlyWinners.RemoveRange(await _db.MonthlyWinners.Where(w => w.Period == p).ToListAsync(ct));
        _db.VoteCampaigns.Remove(campaign);
        await _db.SaveChangesAsync(ct);

        return Ok(new { deleted = id, period = p });
    }

    /// <summary>Clears the votes but keeps the campaign — restart a round without recreating it.</summary>
    [HttpPost("{id:guid}/reset-votes")]
    public async Task<IActionResult> ResetVotes(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null)
            return NotFound(new { error = "CampaignNotFound" });

        var p = campaign.Period;
        var ballots = await _db.MonthlyVoteBallots.Where(b => b.Period == p).ToListAsync(ct);
        _db.MonthlyVoteBallots.RemoveRange(ballots);
        _db.MonthlyVoteTallies.RemoveRange(await _db.MonthlyVoteTallies.Where(t => t.Period == p).ToListAsync(ct));
        _db.MonthlyWinners.RemoveRange(await _db.MonthlyWinners.Where(w => w.Period == p).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);

        return Ok(new { removedVotes = ballots.Count });
    }

    /// <summary>The positions in use, with how many active people hold each — the admin picks from
    /// what exists rather than retyping a title and having it silently match nothing.</summary>
    [HttpGet("positions")]
    public async Task<IActionResult> Positions()
    {
        var rows = await _db.Employees
            .Where(e => e.IsActive && e.Position != null && e.Position != "")
            .GroupBy(e => e.Position!)
            .Select(g => new { position = g.Key, count = g.Count() })
            .OrderBy(x => x.position)
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    private static List<string> Clean(List<string>? positions) =>
        positions?.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList() ?? new List<string>();

    private static string? Validate(VoteCampaignRequest r)
    {
        if (r.EndsOn < r.StartsOn) return "EndBeforeStart";
        // Votes are filed by month; a window spanning two months would split them.
        if (r.StartsOn.Year != r.EndsOn.Year || r.StartsOn.Month != r.EndsOn.Month) return "WindowSpansTwoMonths";
        if (r.MinCandidates < 2) return "MinCandidatesTooLow";
        if (r.MinVotesToDecide < 1) return "MinVotesTooLow";
        return null;
    }
}
