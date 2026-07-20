using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The schedule (qrafik) library — reusable shift templates the admin picks when setting up a
/// location, so the hours aren't retyped every time. Templates only: picking one fills the location's
/// own shift fields (see <see cref="Schedule"/>), so nothing here touches the scan or report paths.
/// Tenant-scoped by the DbContext query filter; Admin only.
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
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(Project(schedule));
    }

    // Deleting a schedule does NOT affect any location — locations hold their own copy of the hours,
    // so this only removes the template from the picker.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var schedule = await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, HttpContext.RequestAborted);
        if (schedule is null)
            return NotFound(new { error = "ScheduleNotFound" });
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
        // Convenience for the UI so it can badge night schedules without re-deriving.
        isOvernight = s.ShiftEnd < s.ShiftStart,
    };
}
