using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// "Bu ay doğum günü olanlar" — this month's birthdays, for the admin. Only employees with a full
/// BirthDate appear (year-only rows can't place a day). Tenant-scoped; Admin only.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/birthdays")]
public class BirthdaysController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public BirthdaysController(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    [HttpGet]
    public async Task<IActionResult> ThisMonth()
    {
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        // Small dataset (staff of one company); pull the ones with a date and filter/sort in memory so
        // there's no DateOnly-part translation to worry about.
        var withDob = await _db.Employees
            .Where(e => e.IsActive && e.BirthDate != null)
            .Select(e => new { e.Id, e.FullName, e.LocationId, Dob = e.BirthDate!.Value })
            .ToListAsync(HttpContext.RequestAborted);

        var locationNames = await _db.Locations
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        var rows = withDob
            .Where(e => e.Dob.Month == todayLocal.Month)
            .OrderBy(e => e.Dob.Day)
            .Select(e => new
            {
                employeeId = e.Id,
                fullName = e.FullName,
                locationName = locationNames.GetValueOrDefault(e.LocationId, ""),
                birthDate = e.Dob,
                day = e.Dob.Day,
                turningAge = todayLocal.Year - e.Dob.Year,
                isToday = e.Dob.Month == todayLocal.Month && e.Dob.Day == todayLocal.Day,
            })
            .ToList();

        return Ok(rows);
    }
}
