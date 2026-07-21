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
/// The company's own "Ayın işçisi" settings, so the owner can move the dates, tighten the thresholds
/// or switch the whole thing off without anyone touching server config.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/vote-settings")]
public class AdminVoteSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IVoteSettingsProvider _provider;
    private readonly TimeZoneInfo _timeZone;

    public AdminVoteSettingsController(AppDbContext db, IVoteSettingsProvider provider, AppOptions options)
    {
        _db = db;
        _provider = provider;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ct = HttpContext.RequestAborted;
        var cfg = await _provider.GetAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        var (open, from, to) = cfg.Window(today);
        return Ok(new
        {
            enabled = cfg.Enabled,
            openDaysBeforeEnd = cfg.OpenDaysBeforeEnd,
            manualFrom = cfg.ManualFrom,
            manualTo = cfg.ManualTo,
            minCandidates = cfg.MinCandidates,
            minVotesToDecide = cfg.MinVotesToDecide,
            // What those settings mean right now — so the admin sees the actual dates, not just a number.
            currentWindowFrom = from,
            currentWindowTo = to,
            isOpenNow = cfg.Enabled && open,
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] VoteSettingsRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (request.OpenDaysBeforeEnd is < 1 or > 28)
            return BadRequest(new { error = "OpenDaysOutOfRange" });
        if (request.MinCandidates < 2)
            return BadRequest(new { error = "MinCandidatesTooLow" });
        if (request.MinVotesToDecide < 1)
            return BadRequest(new { error = "MinVotesTooLow" });
        // A half-set manual window would silently fall back to the automatic one — reject it instead.
        if (request.ManualFrom is null != request.ManualTo is null)
            return BadRequest(new { error = "ManualWindowNeedsBothDates" });
        if (request.ManualFrom is { } f && request.ManualTo is { } t && t < f)
            return BadRequest(new { error = "ManualWindowReversed" });

        var row = await _db.VoteSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new VoteSettings();
            _db.VoteSettings.Add(row);
        }
        row.Enabled = request.Enabled;
        row.OpenDaysBeforeEnd = request.OpenDaysBeforeEnd;
        row.ManualFrom = request.ManualFrom;
        row.ManualTo = request.ManualTo;
        row.MinCandidates = request.MinCandidates;
        row.MinVotesToDecide = request.MinVotesToDecide;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return await Get();
    }

    /// <summary>
    /// Wipes a period's ballot: every vote, the tallies, and any winner already decided. The way out
    /// of a trial run (or a round that went wrong) — without it, test votes would sit in the real
    /// month and a handful of them could crown someone company-wide.
    ///
    /// Deliberately explicit about the period rather than "the current one": deleting the wrong
    /// month's votes is not recoverable.
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] VoteResetRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var period = new DateOnly(request.Period.Year, request.Period.Month, 1);

        var ballots = await _db.MonthlyVoteBallots.Where(b => b.Period == period).ToListAsync(ct);
        var tallies = await _db.MonthlyVoteTallies.Where(t => t.Period == period).ToListAsync(ct);
        var winners = await _db.MonthlyWinners.Where(w => w.Period == period).ToListAsync(ct);

        _db.MonthlyVoteBallots.RemoveRange(ballots);
        _db.MonthlyVoteTallies.RemoveRange(tallies);
        _db.MonthlyWinners.RemoveRange(winners);
        await _db.SaveChangesAsync(ct);

        return Ok(new { period, removedVotes = ballots.Count, removedWinners = winners.Count });
    }
}
