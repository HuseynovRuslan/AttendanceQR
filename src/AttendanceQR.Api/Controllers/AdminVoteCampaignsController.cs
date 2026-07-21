using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
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
    private readonly IVoteAnnouncer _announcer;

    public AdminVoteCampaignsController(AppDbContext db, AppOptions options, IVoteAnnouncer announcer)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _announcer = announcer;
    }

    private DateTime NowLocal() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone);
    private DateOnly TodayLocal() => DateOnly.FromDateTime(NowLocal());

    private object Project(VoteCampaign c, DateTime now, int votes) => new
    {
        id = c.Id,
        period = c.Period,
        startsOn = c.StartsOn,
        endsOn = c.EndsOn,
        startsAt = c.StartsAt.ToString("HH:mm"),
        endsAt = c.EndsAt.ToString("HH:mm"),
        minCandidates = c.MinCandidates,
        minVotesToDecide = c.MinVotesToDecide,
        excludedPositions = c.ExcludedPositions,
        votesCast = votes,
        isOpen = c.IsOpenAt(now),
        // Three distinct states the admin needs to tell apart at a glance.
        state = c.IsOpenAt(now) ? "open" : now < c.OpensAtLocal ? "scheduled" : "finished",
        // Whether employees have been told. Without this the admin cannot tell a ballot that quietly
        // opened from one whose notice went out.
        notified = c.OpenedNotifiedAtUtc is not null,
    };

    /// <summary>The campaign for a month (null when none was created — that month has no ballot).</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateOnly? period)
    {
        var ct = HttpContext.RequestAborted;
        var now = NowLocal();
        var today = TodayLocal();
        var p = period is null ? new DateOnly(today.Year, today.Month, 1) : new DateOnly(period.Value.Year, period.Value.Month, 1);

        var campaign = await _db.VoteCampaigns.FirstOrDefaultAsync(c => c.Period == p, ct);
        if (campaign is null)
            return Ok(new { period = p, campaign = (object?)null });

        var votes = await _db.MonthlyVoteBallots.CountAsync(b => b.Period == p, ct);
        return Ok(new { period = p, campaign = Project(campaign, now, votes) });
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
            StartsAt = ParseTime(request.StartsAt) ?? new TimeOnly(0, 0),
            EndsAt = ParseTime(request.EndsAt) ?? new TimeOnly(23, 59),
            ExcludedPositions = Clean(request.ExcludedPositions),
        };
        _db.VoteCampaigns.Add(campaign);
        await _db.SaveChangesAsync(ct);

        // A ballot created inside its own window is open the moment it is saved — announce it now
        // rather than leaving the admin watching for a notice the next sweep would send minutes later.
        var now = NowLocal();
        await _announcer.AnnounceOpeningAsync(campaign, now, ct);

        return Ok(new { period, campaign = Project(campaign, now, 0) });
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
        campaign.StartsAt = ParseTime(request.StartsAt) ?? new TimeOnly(0, 0);
        campaign.EndsAt = ParseTime(request.EndsAt) ?? new TimeOnly(23, 59);
        campaign.ExcludedPositions = Clean(request.ExcludedPositions);
        await _db.SaveChangesAsync(ct);

        // Editing a scheduled ballot's dates can bring it into the present.
        await _announcer.AnnounceOpeningAsync(campaign, NowLocal(), ct);

        var votes = await _db.MonthlyVoteBallots.CountAsync(b => b.Period == campaign.Period, ct);
        return Ok(new { period = campaign.Period, campaign = Project(campaign, NowLocal(), votes) });
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

    private static List<string> Clean(List<string>? positions) =>
        positions?.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct().ToList() ?? new List<string>();

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParse(value, out var t) ? t : null;

    private static string? Validate(VoteCampaignRequest r)
    {
        if (r.EndsOn < r.StartsOn) return "EndBeforeStart";
        // A same-day window closing before it opens would never be open at all.
        if (r.EndsOn == r.StartsOn && ParseTime(r.EndsAt) is { } end && ParseTime(r.StartsAt) is { } start
            && end <= start) return "EndBeforeStart";
        // Votes are filed by month; a window spanning two months would split them.
        if (r.StartsOn.Year != r.EndsOn.Year || r.StartsOn.Month != r.EndsOn.Month) return "WindowSpansTwoMonths";
        if (r.MinCandidates < 2) return "MinCandidatesTooLow";
        if (r.MinVotesToDecide < 1) return "MinVotesTooLow";
        return null;
    }
}
