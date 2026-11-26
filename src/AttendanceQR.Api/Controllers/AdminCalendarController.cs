using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin-declared non-working days (holidays granted at the manager's discretion, on top of each
/// location's regular weekly WorkDaysMask). Adding/removing one immediately recomputes that date's
/// DailySummary rows so reports agree right away, not just after the next nightly run.
/// </summary>
[Permission(AccessPermissions.HolidaysManage)]
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/non-working-days")]
public class AdminCalendarController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _dailySummaryService;

    public AdminCalendarController(AppDbContext db, IDailySummaryService dailySummaryService)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var days = await _db.NonWorkingDays
            .OrderByDescending(n => n.Date)
            .ToListAsync(HttpContext.RequestAborted);

        var locationNames = await _db.Locations
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        return Ok(days.Select(n => new
        {
            id = n.Id,
            date = n.Date,
            description = n.Description,
            locationId = n.LocationId,
            locationName = n.LocationId != null ? locationNames.GetValueOrDefault(n.LocationId.Value) : null
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NonWorkingDayRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { error = "DescriptionRequired" });

        // A description pasted from the decree that declares the holiday is easily longer than the column. Said
        // here, with the limit, so the admin shortens it and the day is recorded — a failed save leaves the date a
        // working day, and everybody at home on it «Qayıb».
        var description = request.Description.Trim();
        if (FieldLimits.Check<NonWorkingDay>(_db, nameof(NonWorkingDay.Description), description) is { } tooLong)
            return BadRequest(FieldLimitErrors.Body(tooLong));

        if (request.LocationId is not null && !await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var day = new NonWorkingDay
        {
            Date = request.Date,
            Description = description,
            LocationId = request.LocationId
        };
        _db.NonWorkingDays.Add(day);
        await _db.SaveChangesAsync();

        // Reflect immediately in any already-persisted summary for this date, rather than waiting
        // for the next nightly run.
        await _dailySummaryService.GenerateForDateAsync(request.Date, HttpContext.RequestAborted);

        return Ok(new { id = day.Id, date = day.Date, description = day.Description, locationId = day.LocationId });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var day = await _db.NonWorkingDays.FirstOrDefaultAsync(n => n.Id == id);
        if (day is null)
            return NotFound(new { error = "NotFound" });

        var date = day.Date;
        _db.NonWorkingDays.Remove(day);
        await _db.SaveChangesAsync();

        await _dailySummaryService.GenerateForDateAsync(date, HttpContext.RequestAborted);

        return Ok(new { deleted = id });
    }
}
