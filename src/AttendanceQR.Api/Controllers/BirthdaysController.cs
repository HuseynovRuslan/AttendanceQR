using AttendanceQR.Api;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// "Bu ay doğum günü olanlar" — this month's birthdays. Only employees with a full BirthDate appear
/// (year-only rows cannot place a day).
///
/// A branch manager sees this too, narrowed the same way every other manager screen is: the staff of
/// the branches they manage, Role==Employee only. Wishing someone a happy birthday is the least
/// privileged thing in the product, and it was the admin's alone for no reason other than that the
/// screen was written for them.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
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
        var ct = HttpContext.RequestAborted;
        var query = _db.Employees.Where(e => e.IsActive && e.BirthDate != null);

        // Manager scope, from the one place that decides it. Their own row is included because a
        // manager is a person with a birthday too, and the branch list would otherwise omit them
        // (ManagedLocations is what they oversee, not where they clock in).
        if (User.Role() == EmployeeRole.Manager)
        {
            var me = User.EmployeeId();
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, me, ct);
            query = query.Where(e =>
                e.Id == me || (managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee));
        }

        var withDob = await query
            .Select(e => new { e.Id, e.FullName, e.LocationId, Dob = e.BirthDate!.Value })
            .ToListAsync(ct);

        var locationNames = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);

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
