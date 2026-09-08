using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// «Qayıb yaz» — a manager states that somebody did not come to work on a given day.
///
/// Absence used to be inferred: no scan on a scheduled day meant Qayıb, and payroll deducted a day's
/// pay for it. That reads a silence as a fact, and on 2026-09-08 the register showed what it costs —
/// two hundred and twenty-seven active people had never scanned once, and the ten whose fourteen-day
/// setup window had expired were carrying twenty-eight to forty-five absent days each. None of it had
/// been decided by anybody. The inference is gone for people with no attendance history at all
/// (<see cref="AttendanceCalculator.IsStillOnboarding"/>), and this is what takes its place: a person
/// who watched somebody not turn up, with their name on the day and a way to undo it.
///
/// Open to a branch manager, because they are the ones who know. Same boundary as every other manager
/// write: their branches, and plain staff only.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/absence-marks")]
public class AbsenceMarksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _summaries;

    public AbsenceMarksController(AppDbContext db, IDailySummaryService summaries)
    {
        _db = db;
        _summaries = summaries;
    }

    /// <summary>One employee's marked days, recent first — so a profile can show them and undo one.</summary>
    [HttpGet("employee/{employeeId:guid}")]
    public async Task<IActionResult> ForEmployee(Guid employeeId)
    {
        var ct = HttpContext.RequestAborted;
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), employeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var rows = await _db.AbsenceMarks
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.Date)
            .Take(60)
            .Select(a => new { id = a.Id, date = a.Date, note = a.Note, createdAtUtc = a.CreatedAtUtc })
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Marks one day as Qayıb.
    ///
    /// Refused where the day already answers the question by itself. A scan is evidence that they came
    /// and a leave is a decision that they need not have; overruling either from here would let one
    /// screen quietly contradict another, and the one being contradicted is the one holding proof.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Mark([FromBody] AbsenceMarkRequest request)
    {
        var ct = HttpContext.RequestAborted;

        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), request.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4)); // company day, near enough for a guard
        if (request.Date > today)
            return BadRequest(new { error = "DateInFuture" });

        if (await _db.AttendanceRecords.AnyAsync(
                r => r.EmployeeId == request.EmployeeId && r.AttendanceDate == request.Date
                     && r.CheckInAtUtc != null, ct))
            return BadRequest(new { error = "HasRecord" });

        if (await _db.LeaveRecords.AnyAsync(
                l => l.EmployeeId == request.EmployeeId
                     && l.FromDate <= request.Date && l.ToDate >= request.Date, ct))
            return BadRequest(new { error = "HasLeave" });

        var note = request.Note?.Trim();
        note = string.IsNullOrEmpty(note) ? null : note[..Math.Min(note.Length, 200)];

        var existing = await _db.AbsenceMarks
            .FirstOrDefaultAsync(a => a.EmployeeId == request.EmployeeId && a.Date == request.Date, ct);

        if (existing is null)
        {
            _db.AbsenceMarks.Add(new AbsenceMark
            {
                EmployeeId = request.EmployeeId,
                Date = request.Date,
                Note = note,
                CreatedByEmployeeId = User.EmployeeId(),
            });
        }
        else
        {
            existing.Note = note;
            existing.CreatedByEmployeeId = User.EmployeeId();
            existing.CreatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        // Rebuild the day so the tabel and the reports agree at once. A no-op for today by design —
        // today is computed live everywhere, and the board already reads the mark.
        await _summaries.GenerateForDateAsync(request.Date, ct);

        return Ok(new { ok = true });
    }

    /// <summary>Takes the mark off again and rebuilds the day without it.</summary>
    [HttpDelete]
    public async Task<IActionResult> Unmark([FromQuery] Guid employeeId, [FromQuery] DateOnly date)
    {
        var ct = HttpContext.RequestAborted;

        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), employeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var row = await _db.AbsenceMarks
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.Date == date, ct);
        if (row is null)
            return NotFound(new { error = "NotFound" });

        _db.AbsenceMarks.Remove(row);
        await _db.SaveChangesAsync(ct);
        await _summaries.GenerateForDateAsync(date, ct);

        return Ok(new { ok = true });
    }
}
