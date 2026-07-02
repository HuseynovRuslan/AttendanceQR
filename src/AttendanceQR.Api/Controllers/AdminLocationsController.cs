using System.Globalization;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>Location management — list + create/edit/delete. Admin-only.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/locations")]
public class AdminLocationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminLocationsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var locations = await _db.Locations
            .OrderBy(l => l.Name)
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(locations.Select(Project));
    }

    [HttpPost]
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
            LateThresholdMinutes = request.LateThresholdMinutes
        };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync();
        return Ok(Project(location));
    }

    [HttpPut("{id:guid}")]
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
        await _db.SaveChangesAsync();
        return Ok(Project(location));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        // Refuse to delete a location that is still referenced — it would orphan employees /
        // attendance history (and the DB foreign keys would reject it anyway).
        var inUse = await _db.Employees.AnyAsync(e => e.LocationId == id)
                    || await _db.AttendanceRecords.AnyAsync(a => a.LocationId == id)
                    || await _db.DailySummaries.AnyAsync(d => d.LocationId == id);
        if (inUse)
            return Conflict(new { error = "LocationInUse" });

        _db.Locations.Remove(location);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    // Enable/disable without deleting — a disabled location stops issuing kiosk QR and rejects
    // scans, but keeps its employees and history. Use this instead of delete for in-use locations.
    [HttpPut("{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest request)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        location.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(Project(location));
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
        isActive = l.IsActive
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
        return true;
    }
}
