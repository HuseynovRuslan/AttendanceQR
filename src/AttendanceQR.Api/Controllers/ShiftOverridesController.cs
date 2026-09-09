using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// «Əvəzləmə» — one person on somebody else's shift for one day.
///
/// A shift belongs to a PERSON, which is right on the ninety-nine days they work their own hours and
/// wrong on the hundredth. The night a guard is on rest and a colleague covers, the system had
/// nowhere to say so: the night was judged against the cover's own day shift, the record stayed open
/// at zero hours, and the morning exit scan opened a fresh check-in on what was that person's rest
/// day. One night cost him nine hours' pay and a rest day.
///
/// Writing one of these changes nothing about HOW a day is calculated — only which schedule answers
/// for it. Every rule downstream then applies unchanged: the overnight noon-pivot that closes a night
/// with a morning scan, the working-day mask, the late threshold, per-day hours.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/shift-overrides")]
public class ShiftOverridesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _summaries;

    public ShiftOverridesController(AppDbContext db, IDailySummaryService summaries)
    {
        _db = db;
        _summaries = summaries;
    }

    /// <summary>One employee's cover days. Recent first — the ones anybody asks about.</summary>
    [HttpGet("employee/{employeeId:guid}")]
    public async Task<IActionResult> ForEmployee(Guid employeeId)
    {
        var ct = HttpContext.RequestAborted;
        if (!await LocationScopeRules.CanScheduleEmployeeAsync(_db, User.EmployeeId(), User.Role(), employeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var rows = await _db.ShiftOverrides
            .Where(o => o.EmployeeId == employeeId)
            .OrderByDescending(o => o.Date)
            .Take(60)
            .Join(_db.Schedules, o => o.ScheduleId, s => s.Id, (o, s) => new
            {
                id = o.Id,
                date = o.Date,
                scheduleId = s.Id,
                scheduleName = s.Name,
                shiftStart = s.ShiftStart.ToString("HH:mm"),
                shiftEnd = s.ShiftEnd.ToString("HH:mm"),
                note = o.Note,
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Records (or moves) a cover day. Re-callable for the same date: an admin correcting «it was
    /// Gecə B, not Gecə A» must not have to delete a row first, and a second row for one day would
    /// leave two answers to a question that has one.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Set([FromBody] ShiftOverrideRequest request)
    {
        var ct = HttpContext.RequestAborted;

        if (!await LocationScopeRules.CanScheduleEmployeeAsync(_db, User.EmployeeId(), User.Role(), request.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == request.ScheduleId, ct);
        if (schedule is null)
            return BadRequest(new { error = "ScheduleNotFound" });

        // A cover for a day that has not happened yet is ordinary planning — «Saturday you are on
        // nights» — so the future is allowed. Only the absurd is refused.
        if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1))
            return BadRequest(new { error = "DateTooFar" });

        var existing = await _db.ShiftOverrides
            .FirstOrDefaultAsync(o => o.EmployeeId == request.EmployeeId && o.Date == request.Date, ct);

        var note = request.Note?.Trim();
        note = string.IsNullOrEmpty(note) ? null : note[..Math.Min(note.Length, 200)];

        if (existing is null)
        {
            _db.ShiftOverrides.Add(new ShiftOverride
            {
                EmployeeId = request.EmployeeId,
                Date = request.Date,
                ScheduleId = request.ScheduleId,
                Note = note,
                CreatedByEmployeeId = User.EmployeeId(),
            });
        }
        else
        {
            existing.ScheduleId = request.ScheduleId;
            existing.Note = note;
            existing.CreatedByEmployeeId = User.EmployeeId();
            existing.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        // The day is rebuilt at once, so the tabel and the hours agree with the change before anybody
        // navigates away. GenerateForDateAsync is a no-op for today and the future by design — today
        // is live and recomputed on read.
        await _summaries.GenerateForDateAsync(request.Date, ct);

        return Ok(new { ok = true });
    }

    /// <summary>Removes a cover day and rebuilds it against the person's own shift.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var row = await _db.ShiftOverrides.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (row is null)
            return NotFound(new { error = "NotFound" });

        if (!await LocationScopeRules.CanScheduleEmployeeAsync(_db, User.EmployeeId(), User.Role(), row.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var date = row.Date;
        _db.ShiftOverrides.Remove(row);
        await _db.SaveChangesAsync(ct);
        await _summaries.GenerateForDateAsync(date, ct);

        return Ok(new { ok = true });
    }
}
