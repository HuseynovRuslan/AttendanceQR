using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
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
    private readonly TimeZoneInfo _zone;
    private readonly ILogger<AdminLeaveController>? _logger;

    public AdminLeaveController(
        AppDbContext db, IDailySummaryService dailySummaryService, AppOptions appOptions,
        ILogger<AdminLeaveController>? logger = null)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
        _zone = ScheduleRevisions.Zone(appOptions.TimeZone);
        _logger = logger;
    }

    [Permission(AccessPermissions.LeavesView, AtCompany = true)]
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

    [Permission(AccessPermissions.LeavesManage, AtCompany = true)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LeaveRecordRequest request)
    {
        if (request.ToDate < request.FromDate)
            return BadRequest(new { error = "DateRangeInvalid" });
        if (request.ToDate.DayNumber - request.FromDate.DayNumber + 1 > MaxRangeDays)
            return BadRequest(new { error = "DateRangeTooLong" });
        var subjects = request.Subjects;
        if (subjects.Count == 0)
            return BadRequest(new { error = "EmployeeNotFound" });

        var ct = HttpContext.RequestAborted;
        var people = await _db.Employees
            .Where(e => subjects.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName })
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        if (people.Count == 0)
            return BadRequest(new { error = "EmployeeNotFound" });

        // Everything these people already have that touches the range, in one read rather than one
        // per person — a crew of forty would otherwise be forty round trips for a rest day.
        var existing = await _db.LeaveRecords
            .Where(l => subjects.Contains(l.EmployeeId)
                        && l.FromDate <= request.ToDate && l.ToDate >= request.FromDate)
            .ToListAsync(ct);

        var requesterId = User.EmployeeId();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        // The column holds 500 (LeaveRecords.Note). Checked here because the same note goes onto every row of the
        // batch: one pasted doctor's note over the limit used to fail the whole crew's save with a 500 and no reason.
        if (note is { Length: > LeaveNote.MaxLength })
            return BadRequest(new { error = LeaveNote.TooLong, max = LeaveNote.MaxLength });
        var created = new List<object>();
        var skipped = new List<object>();

        foreach (var employeeId in subjects)
        {
            if (!people.TryGetValue(employeeId, out var fullName))
            {
                skipped.Add(new { employeeId, fullName = "", reason = "EmployeeNotFound" });
                continue;
            }

            // Already off over these days. Skipped rather than failing the whole batch: one person in
            // a crew who is already on holiday must not stop the other thirty-nine being marked.
            var clash = existing.FirstOrDefault(l =>
                l.EmployeeId == employeeId && LeaveOverlapRule.Overlaps(l, request.FromDate, request.ToDate));
            if (clash is not null)
            {
                skipped.Add(new
                {
                    employeeId,
                    fullName,
                    reason = "Overlaps",
                    conflictType = clash.Type.ToString(),
                    conflictFrom = clash.FromDate,
                    conflictTo = clash.ToDate,
                });
                continue;
            }

            var leave = new LeaveRecord
            {
                EmployeeId = employeeId,
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                Type = request.Type,
                Note = note,
                CreatedByEmployeeId = requesterId,
            };
            _db.LeaveRecords.Add(leave);
            created.Add(new { id = leave.Id, employeeId, fullName });
        }

        if (created.Count == 0)
            return Conflict(new { error = "AllOverlap", skipped });

        // One recompute for the whole batch — the range is the same for everybody in it. The past days are noted in
        // the same save as the leave, so an abandoned request cannot leave them reading Qayıb (PlanRebuilds).
        var stale = PastDays(request.FromDate, request.ToDate);
        await PlanRebuilds.SaveWithNotesAsync(_db, stale, ct);
        await PlanRebuilds.FinishAsync(_db, _dailySummaryService, stale, _logger);

        return Ok(new
        {
            created,
            skipped,
            fromDate = request.FromDate,
            toDate = request.ToDate,
            type = request.Type.ToString(),
        });
    }

    [Permission(AccessPermissions.LeavesManage, AtCompany = true)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var leave = await _db.LeaveRecords.FirstOrDefaultAsync(l => l.Id == id);
        if (leave is null)
            return NotFound(new { error = "NotFound" });

        var stale = PastDays(leave.FromDate, leave.ToDate);
        _db.LeaveRecords.Remove(leave);
        await PlanRebuilds.SaveWithNotesAsync(_db, stale, HttpContext.RequestAborted);
        await PlanRebuilds.FinishAsync(_db, _dailySummaryService, stale, _logger);

        return Ok(new { deleted = id });
    }

    private List<DateOnly> PastDays(DateOnly from, DateOnly to) =>
        PlanRebuilds.PastDays(from, to, ScheduleRevisions.Today(_zone));
}
