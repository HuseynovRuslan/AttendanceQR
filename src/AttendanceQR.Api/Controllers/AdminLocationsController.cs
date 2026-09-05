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
// A branch manager sees, edits and prints the poster for the branches they manage. Creating and
// deleting branches stays with the admin, and that line is commercial rather than technical: a branch
// costs the customer 5 ₼ a month, so adding one is the decision of whoever pays the bill.
//
// Editing a geofence is the interesting permission. It IS the anti-fraud boundary — a manager who
// moved it onto their own house could clock in from there with their whole crew — and the honest
// answer is not to refuse it. Refusing it leaves nine sites on coordinates an admin guessed from an
// office, which is what put the wrong poster on a wall at Dədə Qorqud Parkı and cost a day of scans.
// The manager is the one who knows where the poster hangs and how wide the yard is.
//
// So the move is allowed and RECORDED: stamped on the branch (who, when, how many metres) and written
// to the audit log in full. Detection rather than prevention — which is also the first time an
// ADMIN's move has been recorded, because until now nothing anywhere was.
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
    private readonly IFaceMatchService _faceMatch;
    private readonly ILogger<AdminLocationsController> _logger;

    public AdminLocationsController(AppDbContext db, IQrTokenService qrTokenService,
        IFaceMatchService faceMatch, ILogger<AdminLocationsController> logger)
    {
        _db = db;
        _qrTokenService = qrTokenService;
        _faceMatch = faceMatch;
        _logger = logger;
    }

    /// <summary>
    /// A branch without a poster leans on the selfie as its only anchor. With the face service off
    /// that anchor does not exist: the check-in would be GPS-only, no verdict, no flag, and nothing on
    /// any screen saying so. Refused rather than allowed to fail silently.
    /// </summary>
    private IActionResult? RefuseQrlessWithoutFaceService(bool? wanted) =>
        wanted == true && !_faceMatch.Enabled ? BadRequest(new { error = "FaceMatchDisabled" }) : null;

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
        if (RefuseQrlessWithoutFaceService(request.QrlessCheckIn) is { } refused)
            return refused;

        var location = new Location
        {
            Name = request.Name.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = request.RadiusMeters,
            ShiftStart = start,
            ShiftEnd = end,
            LateThresholdMinutes = request.LateThresholdMinutes,
            WorkDaysMask = request.WorkDaysMask,
            QrlessCheckIn = request.QrlessCheckIn ?? false,
            RequireGeofence = request.RequireGeofence ?? true
        };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync();
        return Ok(Project(location));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LocationRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        if (await OutOfScopeAsync(location.Id, ct) is { } refusal)
            return refusal;

        if (!TryValidate(request, out var start, out var end, out var error))
            return BadRequest(new { error });
        if (RefuseQrlessWithoutFaceService(request.QrlessCheckIn) is { } refused)
            return refused;

        // Read the old fence BEFORE overwriting it — the distance is the whole point of the record,
        // and after the assignment below there is nothing left to measure against.
        var (oldLat, oldLng, oldRadius) = (location.Latitude, location.Longitude, location.RadiusMeters);
        var moved = GeoCalculator.DistanceMeters(oldLat, oldLng, request.Latitude, request.Longitude);
        var fenceChanged = moved >= 1 || oldRadius != request.RadiusMeters;

        location.Name = request.Name.Trim();
        location.Latitude = request.Latitude;
        location.Longitude = request.Longitude;
        location.RadiusMeters = request.RadiusMeters;
        location.ShiftStart = start;
        location.ShiftEnd = end;
        location.LateThresholdMinutes = request.LateThresholdMinutes;
        location.WorkDaysMask = request.WorkDaysMask;
        if (request.RequireGeofence is bool fence && fence != location.RequireGeofence)
        {
            // Louder than the flag above: with this off the branch has no location gate at all.
            _logger.LogWarning("Location {LocationId} ({Name}): RequireGeofence {From} -> {To}",
                location.Id, location.Name, location.RequireGeofence, fence);
            location.RequireGeofence = fence;
        }
        if (request.QrlessCheckIn is bool qrless && qrless != location.QrlessCheckIn)
        {
            // Loud either way: this flips how every person at the branch records their day.
            _logger.LogWarning("Location {LocationId} ({Name}): QrlessCheckIn {From} -> {To}",
                location.Id, location.Name, location.QrlessCheckIn, qrless);
            location.QrlessCheckIn = qrless;
        }

        if (fenceChanged)
        {
            var by = User.EmployeeId();
            location.GeofenceMovedAtUtc = DateTime.UtcNow;
            location.GeofenceMovedByEmployeeId = by;
            location.GeofenceMovedMeters = (int)Math.Round(moved);

            _db.AuditLogs.Add(new AuditLog
            {
                EmployeeId = by,
                EventType = AuditEventType.LocationMoved,
                Reason = $"{location.Name}: {oldLat:F5},{oldLng:F5} r{oldRadius}m → "
                         + $"{request.Latitude:F5},{request.Longitude:F5} r{request.RadiusMeters}m "
                         + $"({Math.Round(moved)} m)",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(Project(location));
    }

    /// <summary>
    /// A refusal when this caller may not touch this branch, or null when they may. An admin may touch
    /// any of their company's; a manager only the ones they manage — the same ManagedLocations set
    /// that decides everything else they see.
    /// </summary>
    private async Task<IActionResult?> OutOfScopeAsync(Guid locationId, CancellationToken ct)
    {
        if (User.Role() != EmployeeRole.Manager)
            return null;
        var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), ct);
        return managed.Contains(locationId)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
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
    // A manager prints their own branch's poster. It is the strongest argument in this whole change:
    // the posters hanging at Dədə Qorqud Parkı belonged to two OTHER branches, so nobody at that site
    // could clock in — and the person standing in front of the wall could not fix it. Invalidating the
    // QR stays with the admin below, because that one voids every printed poster at once.
    [HttpPost("{id:guid}/static-qr")]
    public async Task<IActionResult> GenerateStaticQr(Guid id)
    {
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, HttpContext.RequestAborted);
        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        if (await OutOfScopeAsync(location.Id, HttpContext.RequestAborted) is { } refusal)
            return refusal;

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
        geofenceMovedAtUtc = l.GeofenceMovedAtUtc,
        geofenceMovedMeters = l.GeofenceMovedMeters,
        id = l.Id,
        name = l.Name,
        latitude = l.Latitude,
        longitude = l.Longitude,
        radiusMeters = l.RadiusMeters,
        shiftStart = l.ShiftStart.ToString("HH:mm"),
        shiftEnd = l.ShiftEnd.ToString("HH:mm"),
        lateThresholdMinutes = l.LateThresholdMinutes,
        isActive = l.IsActive,
        workDaysMask = l.WorkDaysMask,
        qrlessCheckIn = l.QrlessCheckIn,
        requireGeofence = l.RequireGeofence
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
