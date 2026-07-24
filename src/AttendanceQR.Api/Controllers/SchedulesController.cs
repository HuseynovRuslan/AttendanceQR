using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Named shifts ("növbə") — hours, working days and an optional rotation, defined once and assigned
/// to employees. Tenant-scoped by the DbContext query filter; Admin only.
///
/// These are LIVE now, not templates. Editing one changes how every employee on it is judged,
/// including on days already past, because reports resolve through the shift rather than through a
/// copy taken at the time. Both the delete guard below and the admin UI say so.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/schedules")]
public class SchedulesController : ControllerBase
{
    private readonly AppDbContext _db;

    public SchedulesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.Schedules
            .OrderBy(s => s.CreatedAtUtc)
            .Select(s => Project(s))
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ScheduleRequest request)
    {
        if (!TryParse(request, out var start, out var end, out var error))
            return BadRequest(new { error });

        var schedule = new Schedule
        {
            Name = request.Name.Trim(),
            ShiftStart = start,
            ShiftEnd = end,
            LateThresholdMinutes = request.LateThresholdMinutes,
            WorkDaysMask = request.WorkDaysMask,
        };
        if (WorkCycle.Apply(schedule, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(Project(schedule));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ScheduleRequest request)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, HttpContext.RequestAborted);
        if (schedule is null)
            return NotFound(new { error = "ScheduleNotFound" });
        if (!TryParse(request, out var start, out var end, out var error))
            return BadRequest(new { error });

        schedule.Name = request.Name.Trim();
        schedule.ShiftStart = start;
        schedule.ShiftEnd = end;
        schedule.LateThresholdMinutes = request.LateThresholdMinutes;
        schedule.WorkDaysMask = request.WorkDaysMask;
        if (WorkCycle.Apply(schedule, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(Project(schedule));
    }

    // Refused while anyone is on the shift. There is no foreign key doing this for us, so deleting a
    // shift in use would leave those employees pointing at nothing — they would silently fall back to
    // their branch's hours, which is a change to how their pay is calculated that nobody asked for.
    // The count comes back with the error so the UI can say who is affected.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, HttpContext.RequestAborted);
        if (schedule is null)
            return NotFound(new { error = "ScheduleNotFound" });

        var assigned = await _db.Employees.CountAsync(e => e.ScheduleId == id, HttpContext.RequestAborted);
        if (assigned > 0)
            return Conflict(new { error = "ScheduleInUse", employeeCount = assigned });

        _db.Schedules.Remove(schedule);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { deleted = id });
    }

    private static bool TryParse(ScheduleRequest r, out TimeOnly start, out TimeOnly end, out string? error)
    {
        start = default; end = default; error = null;
        if (string.IsNullOrWhiteSpace(r.Name))
        {
            error = "NameRequired";
            return false;
        }
        if (!TimeOnly.TryParse(r.ShiftStart, out start))
        {
            error = "ShiftStartInvalid";
            return false;
        }
        if (!TimeOnly.TryParse(r.ShiftEnd, out end))
        {
            error = "ShiftEndInvalid";
            return false;
        }
        if (r.LateThresholdMinutes < 0)
        {
            error = "LateThresholdNegative";
            return false;
        }
        return true;
    }

    private static object Project(Schedule s) => new
    {
        id = s.Id,
        name = s.Name,
        shiftStart = s.ShiftStart.ToString("HH:mm"),
        shiftEnd = s.ShiftEnd.ToString("HH:mm"),
        lateThresholdMinutes = s.LateThresholdMinutes,
        workDaysMask = s.WorkDaysMask,
        workCycleDays = s.WorkCycleDays,
        workCycleOnDays = s.WorkCycleOnDays,
        workCycleAnchor = s.WorkCycleAnchor,
        // Convenience for the UI so it can badge night schedules without re-deriving.
        isOvernight = s.ShiftEnd < s.ShiftStart,
    };
}
