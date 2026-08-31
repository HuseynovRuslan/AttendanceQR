using AttendanceQR.Api.Contracts;
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
/// Named shifts ("növbə") — hours, working days and an optional rotation, defined once and assigned
/// to employees. Tenant-scoped by the DbContext query filter; Admin only.
///
/// These are LIVE now, not templates. Editing one changes how every employee on it is judged,
/// including on days already past, because reports resolve through the shift rather than through a
/// copy taken at the time. Both the delete guard below and the admin UI say so.
/// </summary>
[ApiController]
// A branch manager may READ the shift templates — the sidebar has always offered them this screen,
// and until now clicking it returned 403, because the class gate said Admin while the menu said
// otherwise. Reading is what they need: a manager looking at a late arrival has to know which shift
// that person is on, and they see the company's shared shifts plus their own branches' — not another
// branch's crew. Writing stays with the admin: a shared shift belongs to every branch at once, and a
// branch's own shift is still the company's to define.
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/schedules")]
public class SchedulesController : ControllerBase
{
    private readonly AppDbContext _db;

    public SchedulesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var ct = HttpContext.RequestAborted;
        var query = _db.Schedules.AsQueryable();

        if (User.Role() == EmployeeRole.Manager)
        {
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), ct);
            query = query.Where(s => s.LocationId == null || managed.Contains(s.LocationId.Value));
        }

        // The branch name comes back with the row so the picker can say "FM 2-ci növbə · Fəvvarələr
        // Meydanı" without a second call and without the name having to carry the branch itself.
        var rows = await query
            .OrderBy(s => s.LocationId == null ? 0 : 1)
            .ThenBy(s => s.CreatedAtUtc)
            .Select(s => new
            {
                schedule = s,
                locationName = _db.Locations.Where(l => l.Id == s.LocationId).Select(l => l.Name).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => Project(r.schedule, r.locationName)));
    }

    /// <summary>The branch a shift is being pinned to, checked to exist in THIS company.</summary>
    private async Task<string?> ApplyLocationAsync(Schedule schedule, Guid? locationId)
    {
        if (locationId is not Guid id)
        {
            schedule.LocationId = null;
            return null;
        }
        // Tenant-filtered, so a branch id belonging to another company reads as not found.
        if (!await _db.Locations.AnyAsync(l => l.Id == id, HttpContext.RequestAborted))
            return "LocationNotFound";
        schedule.LocationId = id;
        return null;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
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
        if (await ApplyLocationAsync(schedule, request.LocationId) is { } locationError)
            return BadRequest(new { error = locationError });
        if (WorkCycle.Apply(schedule, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });

        if (ScheduleDayHours.Apply(schedule, request.DayHours) is { } dayHoursError)
            return BadRequest(new { error = dayHoursError });
        _db.Schedules.Add(schedule);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(Project(schedule));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
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
        if (await ApplyLocationAsync(schedule, request.LocationId) is { } locationError)
            return BadRequest(new { error = locationError });
        if (WorkCycle.Apply(schedule, request.WorkCycleDays, request.WorkCycleOnDays, request.WorkCycleAnchor) is { } cycleError)
            return BadRequest(new { error = cycleError });

        if (ScheduleDayHours.Apply(schedule, request.DayHours) is { } dayHoursError)
            return BadRequest(new { error = dayHoursError });

        // Moving a shift to a branch strands anyone already on it who works somewhere else — their
        // hours would come from a shift their branch no longer offers. Refused with the count, the
        // same shape as deleting a shift that is in use.
        if (schedule.LocationId is Guid pinned)
        {
            var stranded = await _db.Employees
                .CountAsync(e => e.ScheduleId == schedule.Id && e.LocationId != pinned, HttpContext.RequestAborted);
            if (stranded > 0)
                return Conflict(new { error = "ScheduleUsedByOtherBranch", employeeCount = stranded });
        }

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(Project(schedule));
    }

    // Refused while anyone is on the shift. There is no foreign key doing this for us, so deleting a
    // shift in use would leave those employees pointing at nothing — they would silently fall back to
    // their branch's hours, which is a change to how their pay is calculated that nobody asked for.
    // The count comes back with the error so the UI can say who is affected.
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
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

    private static object Project(Schedule s, string? locationName = null) => new
    {
        id = s.Id,
        name = s.Name,
        locationId = s.LocationId,
        // Null for a shift the whole company shares.
        locationName,
        shiftStart = s.ShiftStart.ToString("HH:mm"),
        shiftEnd = s.ShiftEnd.ToString("HH:mm"),
        lateThresholdMinutes = s.LateThresholdMinutes,
        workDaysMask = s.WorkDaysMask,
        workCycleDays = s.WorkCycleDays,
        workCycleOnDays = s.WorkCycleOnDays,
        workCycleAnchor = s.WorkCycleAnchor,
        // Convenience for the UI so it can badge night schedules without re-deriving.
        isOvernight = s.ShiftEnd < s.ShiftStart,
        // Days with their own hours, keyed by day number, so the form can round-trip them without
        // parsing the stored string itself.
        dayHours = AttendanceQR.Application.Reporting.DayHours.Parse(s.DayHours)
            .ToDictionary(
                kv => ((int)kv.Key).ToString(),
                kv => new { start = kv.Value.Start.ToString(@"HH\:mm"), end = kv.Value.End.ToString(@"HH\:mm") }),
    };
}
