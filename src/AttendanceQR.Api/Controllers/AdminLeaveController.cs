using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin-approved leave/permission ranges. Adding or removing one immediately recomputes every
/// date in its range's DailySummary rows so reports agree right away, not just after the next
/// nightly run.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/leaves")]
public class AdminLeaveController : ControllerBase
{
    // A year comfortably covers any real vacation/sick/permission range; beyond that it's almost
    // certainly a typo, and recomputing DailySummary for every employee on every date in the
    // range is O(days), so an unbounded range is a real cost, not just a data-quality issue.
    private const int MaxRangeDays = 366;

    private readonly AppDbContext _db;
    private readonly IDailySummaryService _dailySummaryService;

    public AdminLeaveController(AppDbContext db, IDailySummaryService dailySummaryService)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? employeeId)
    {
        var query = _db.LeaveRecords.AsQueryable();
        if (from is not null)
            query = query.Where(l => l.ToDate >= from);
        if (to is not null)
            query = query.Where(l => l.FromDate <= to);
        if (employeeId is not null)
            query = query.Where(l => l.EmployeeId == employeeId);

        var leaves = await query.OrderByDescending(l => l.FromDate).ToListAsync(HttpContext.RequestAborted);

        var employeeNames = await _db.Employees
            .ToDictionaryAsync(e => e.Id, e => e.FullName, HttpContext.RequestAborted);

        return Ok(leaves.Select(l => new
        {
            id = l.Id,
            employeeId = l.EmployeeId,
            employeeName = employeeNames.GetValueOrDefault(l.EmployeeId, "—"),
            fromDate = l.FromDate,
            toDate = l.ToDate,
            type = l.Type.ToString(),
            note = l.Note,
            createdAtUtc = l.CreatedAtUtc
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LeaveRecordRequest request)
    {
        if (request.ToDate < request.FromDate)
            return BadRequest(new { error = "DateRangeInvalid" });
        if (request.ToDate.DayNumber - request.FromDate.DayNumber + 1 > MaxRangeDays)
            return BadRequest(new { error = "DateRangeTooLong" });
        if (!await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
            return BadRequest(new { error = "EmployeeNotFound" });

        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            return Unauthorized(new { error = "InvalidToken" });

        var leave = new LeaveRecord
        {
            EmployeeId = request.EmployeeId,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Type = request.Type,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByEmployeeId = requesterId
        };
        _db.LeaveRecords.Add(leave);
        await _db.SaveChangesAsync();

        await RecomputeRangeAsync(request.FromDate, request.ToDate);

        return Ok(new
        {
            id = leave.Id,
            employeeId = leave.EmployeeId,
            fromDate = leave.FromDate,
            toDate = leave.ToDate,
            type = leave.Type.ToString(),
            note = leave.Note
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var leave = await _db.LeaveRecords.FirstOrDefaultAsync(l => l.Id == id);
        if (leave is null)
            return NotFound(new { error = "NotFound" });

        var (from, to) = (leave.FromDate, leave.ToDate);
        _db.LeaveRecords.Remove(leave);
        await _db.SaveChangesAsync();

        await RecomputeRangeAsync(from, to);

        return Ok(new { deleted = id });
    }

    private async Task RecomputeRangeAsync(DateOnly from, DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
            await _dailySummaryService.GenerateForDateAsync(date, HttpContext.RequestAborted);
    }
}
