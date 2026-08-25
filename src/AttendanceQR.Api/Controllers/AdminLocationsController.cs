using System.Globalization;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>Location management — list + create/edit/delete. Admin-only.</summary>
[ApiController]
// The LIST is open to a branch manager, narrowed to the branches they manage — the dashboard's own
// filter needs it, and a manager who cannot see their branch's name or print its QR poster is missing
// the two things the screen is for. Everything that CHANGES a branch stays with the admin, and that is
// not squeamishness: the geofence is the anti-fraud boundary, and a manager who could move it could
// move it to their own house and clock in from there.
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/locations")]
public class AdminLocationsController : ControllerBase
{
    // A printed poster needs to survive well past its next replacement cycle without becoming a
    // permanent, un-revocable secret. 60 days gives comfortable buffer for a ~monthly reprint (so
    // the poster never expires before it's replaced), while QrVersion still lets an admin invalidate
    // it sooner if needed (a leaked photo, a lost poster, etc).
    private const int StaticQrTtlSeconds = 60 * 24 * 60 * 60;

    private readonly AppDbContext _db;
    private readonly IQrTokenService _qrTokenService;

    public AdminLocationsController(AppDbContext db, IQrTokenService qrTokenService)
    {
        _db = db;
        _qrTokenService = qrTokenService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var ct = HttpContext.RequestAborted;
        var query = _db.Locations.AsQueryable();

        if (User.Role() == EmployeeRole.Manager)
        {
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), ct);
            query = query.Where(l => managed.Contains(l.Id));
        }

        var locations = await query.OrderBy(l => l.Name).ToListAsync(ct);
        return Ok(locations.Select(Project));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] LocationRequest request)
    {
        if (!TryValidate(request, out var start, out var end, out var error))
            return BadRequest(new { error });

        var location = new Location
        {
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = request.RadiusMeters,
            ShiftStart = start,
            ShiftEnd = end,
            LateThresholdMinutes = request.LateThresholdMinutes,
            WorkDaysMask = request.WorkDaysMask
        };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync();
        return Ok(Project(location));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LocationRequest request)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        if (!TryValidate(request, out var start, out var end, out var error))
            return BadRequest(new { error });

        location.Name = request.Name.Trim();
        location.Latitude = request.Latitude;
        location.Longitude = request.Longitude;
        location.RadiusMeters = request.RadiusMeters;
        location.ShiftStart = start;
        location.ShiftEnd = end;
        location.LateThresholdMinutes = request.LateThresholdMinutes;
        location.WorkDaysMask = request.WorkDaysMask;
        await _db.SaveChangesAsync();
        return Ok(Project(location));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        // Refuse to delete a location that is still referenced — it would orphan employees or the
        // attendance history (and the DB foreign keys would reject it anyway).
        //
        // Report WHAT is holding it, because the two cases have different answers and the admin cannot
        // see which they are in: staff can be moved and then the branch deletes, but history never can
        // — that branch has to be deactivated instead. "Cannot be deleted" on its own left someone
        // staring at a branch whose only occupant was their own admin account.
        var ct = HttpContext.RequestAborted;
        var employeeCount = await _db.Employees.CountAsync(e => e.LocationId == id, ct);
        var historyCount = await _db.AttendanceRecords.CountAsync(a => a.LocationId == id, ct);
        if (employeeCount > 0 || historyCount > 0)
            return Conflict(new { error = "LocationInUse", employeeCount, historyCount });

        // DailySummaries used to count as history here, and that is what made empty branches
        // undeletable for months: a summary is DERIVED — the night job writes one per employee per day,
        // including days nobody scanned — and it keeps pointing at whichever branch the employee was in
        // that day, long after they moved. So a branch with nobody in it and not one scan against it
        // still had rows, and the answer the admin got was "istifadə olunur", about a branch that was
        // demonstrably not.
        //
        // With no employees and no scans left there is nothing to protect, so the derived rows follow
        // the employee: the day and its status stay (they are payroll-relevant — an absence is money),
        // only the branch they are filed under moves to where that person is now. A summary whose
        // employee is gone has nothing left to describe and goes with the branch.
        var summaries = await _db.DailySummaries.Where(d => d.LocationId == id).ToListAsync(ct);
        var moved = 0;
        var dropped = 0;
        if (summaries.Count > 0)
        {
            var employeeIds = summaries.Select(d => d.EmployeeId).Distinct().ToList();
            var currentLocation = await _db.Employees
                .Where(e => employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.LocationId, ct);

            foreach (var summary in summaries)
            {
                if (currentLocation.TryGetValue(summary.EmployeeId, out var location2) && location2 != id)
                {
                    summary.LocationId = location2;
                    moved++;
                }
                else
                {
                    _db.DailySummaries.Remove(summary);
                    dropped++;
                }
            }
        }

        _db.Locations.Remove(location);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = id, summariesMoved = moved, summariesRemoved = dropped });
    }

    // Enable/disable without deleting — a disabled location stops issuing kiosk QR and rejects
    // scans, but keeps its employees and history. Use this instead of delete for in-use locations.
    [HttpPut("{id:guid}/active")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest request)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        location.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(Project(location));
    }

    // Long-lived QR meant to be printed and posted at the location (unlike the kiosk's 60s-rotating
    // one). Same crypto/scan path as the kiosk token — just a longer TTL — so it works with the
    // employee's existing scan flow with no special-casing.
    [HttpPost("{id:guid}/static-qr")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GenerateStaticQr(Guid id)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        var token = _qrTokenService.Generate(id, location.QrVersion, StaticQrTtlSeconds);
        var expiresAtUtc = DateTime.UtcNow.AddSeconds(StaticQrTtlSeconds);
        return Ok(new { token, expiresAtUtc, locationName = location.Name });
    }

    // Instantly revokes every outstanding QR for this location — the kiosk's rotating code AND any
    // printed static poster — by bumping the version every issued token must match. Use when a
    // poster is lost/leaked, or just to force a fresh print cycle.
    [HttpPost("{id:guid}/invalidate-qr")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> InvalidateQr(Guid id)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        location.QrVersion++;
        await _db.SaveChangesAsync();
        return Ok(new { locationId = id, qrVersion = location.QrVersion });
    }

    private static object Project(Location l) => new
    {
        id = l.Id,
        name = l.Name,
        latitude = l.Latitude,
        longitude = l.Longitude,
        radiusMeters = l.RadiusMeters,
        shiftStart = l.ShiftStart.ToString("HH:mm"),
        shiftEnd = l.ShiftEnd.ToString("HH:mm"),
        lateThresholdMinutes = l.LateThresholdMinutes,
        isActive = l.IsActive,
        workDaysMask = l.WorkDaysMask
    };

    private static bool TryValidate(LocationRequest r, out TimeOnly start, out TimeOnly end, out string? error)
    {
        start = default;
        end = default;
        error = null;
        if (string.IsNullOrWhiteSpace(r.Name)) { error = "NameRequired"; return false; }
        if (r.Latitude is < -90 or > 90) { error = "LatitudeOutOfRange"; return false; }
        if (r.Longitude is < -180 or > 180) { error = "LongitudeOutOfRange"; return false; }
        if (r.RadiusMeters <= 0) { error = "RadiusMustBePositive"; return false; }
        if (r.LateThresholdMinutes < 0) { error = "LateThresholdNegative"; return false; }
        if (!TimeOnly.TryParse(r.ShiftStart, CultureInfo.InvariantCulture, out start)) { error = "ShiftStartInvalid"; return false; }
        if (!TimeOnly.TryParse(r.ShiftEnd, CultureInfo.InvariantCulture, out end)) { error = "ShiftEndInvalid"; return false; }
        if (r.WorkDaysMask is < 0 or > 127) { error = "WorkDaysMaskInvalid"; return false; }
        return true;
    }
}
