using System.Security.Claims;
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

[ApiController]
[Authorize]
[Route("api/attendance")]
public class AttendanceController : ControllerBase
{
    // A second scan within this many minutes of check-in is treated as an accidental double-scan,
    // not a check-out — stops an employee being checked straight back out seconds after arriving.
    private const int MinCheckoutMinutes = 5;

    // Reasons the client may self-report against its own account. An allow-list, not free text:
    // the body is employee-controlled and lands straight in the audit log the admin panel reads.
    private static readonly string[] ClientFailureReasons =
        ["GpsPermissionDenied", "GpsUnavailable", "GpsTimeout", "GpsUnsupported", "GpsInaccurate"];

    // A blocked employee retries over and over. Collapse the same (employee, reason) into one
    // incident for this long, so one stuck phone doesn't bury the day's real problems.
    private static readonly TimeSpan FailureDedupeWindow = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly IQrTokenService _qrTokenService;
    private readonly IAttendanceQueryService _attendanceQuery;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IFaceMatchQueue _faceQueue;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(
        AppDbContext db,
        IQrTokenService qrTokenService,
        IAttendanceQueryService attendanceQuery,
        IPhotoStorageService photoStorage,
        IFaceMatchQueue faceQueue,
        ILogger<AttendanceController> logger)
    {
        _db = db;
        _qrTokenService = qrTokenService;
        _attendanceQuery = attendanceQuery;
        _photoStorage = photoStorage;
        _faceQueue = faceQueue;
        _logger = logger;
    }

    // GET /api/attendance/me — the caller's own records. Identity is the JWT "sub"; there is no
    // way to ask for anyone else's here.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        var records = await _attendanceQuery.GetOwnRecordsAsync(employeeId, HttpContext.RequestAborted);
        return Ok(records);
    }

    // GET /api/attendance/me/profile — the caller's own profile (name/location) for the mobile
    // home greeting + menu card. The JWT only carries id/email/role, so name comes from here.
    [HttpGet("me/profile")]
    public async Task<IActionResult> MyProfile()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        var profile = await _db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => new
            {
                fullName = e.FullName,
                email = e.Email,
                role = e.Role.ToString(),
                position = e.Position,
                locationName = _db.Locations
                    .Where(l => l.Id == e.LocationId)
                    .Select(l => l.Name)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (profile is null)
            return NotFound(new { error = "NotFound" });

        return Ok(profile);
    }

    // GET /api/attendance/employee/{id} — another employee's records, subject to a resource-level
    // check in the service ([Authorize] alone cannot enforce "only your own / your team's").
    [HttpGet("employee/{employeeId:guid}")]
    public async Task<IActionResult> ForEmployee(Guid employeeId)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            return Unauthorized(new { error = "InvalidToken" });

        if (!Enum.TryParse<EmployeeRole>(User.FindFirstValue("role"), out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var (access, records) = await _attendanceQuery.GetForEmployeeAsync(
            employeeId, requesterId, role, HttpContext.RequestAborted);

        if (access == AttendanceAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(records);
    }

    // GET /api/attendance/{recordId}/photo-url — short-lived presigned URLs for the check-in selfie
    // and the employee's reference selfie, so a manager/admin can eyeball them side by side. Photos
    // never pass through the DB or this API; the browser loads them straight from MinIO. Authorization
    // reuses the same location-scope rule as the record read side.
    [HttpGet("{recordId:guid}/photo-url")]
    public async Task<IActionResult> PhotoUrl(Guid recordId)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            return Unauthorized(new { error = "InvalidToken" });
        if (!Enum.TryParse<EmployeeRole>(User.FindFirstValue("role"), out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var ct = HttpContext.RequestAborted;

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        if (!await LocationScopeRules.CanAccessEmployeeAsync(_db, requesterId, role, record.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var referenceKey = await _db.Employees
            .Where(e => e.Id == record.EmployeeId)
            .Select(e => e.ReferencePhotoKey)
            .FirstOrDefaultAsync(ct);

        var checkInUrl = record.CheckInPhotoKey is null
            ? null
            : await _photoStorage.GetPresignedUrlAsync(record.CheckInPhotoKey, ct);
        var referenceUrl = referenceKey is null
            ? null
            : await _photoStorage.GetPresignedUrlAsync(referenceKey, ct);

        return Ok(new
        {
            hasPhoto = checkInUrl is not null,
            checkInPhotoUrl = checkInUrl,
            checkInPhotoTakenAtUtc = record.CheckInPhotoTakenAtUtc,
            referencePhotoUrl = referenceUrl,
            faceMatchScore = record.FaceMatchScore,
            faceMatchStatus = record.FaceMatchStatus.ToString()
        });
    }

    // POST /api/attendance/scan-failure — the scan never happened: the browser would not give the
    // client a position, so there is no QR token, no coordinates and nothing to validate. Recorded
    // so the employee surfaces in the admin "Problemlər" screen rather than failing silently all
    // morning while nobody knows. Advisory only — it can never create or alter an attendance record.
    [HttpPost("scan-failure")]
    public async Task<IActionResult> ScanFailure([FromBody] ScanFailureRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        if (string.IsNullOrEmpty(request.Reason) || !ClientFailureReasons.Contains(request.Reason, StringComparer.Ordinal))
            return BadRequest(new { error = "UnknownReason" });

        var since = DateTime.UtcNow - FailureDedupeWindow;
        var alreadyLogged = await _db.AuditLogs.AnyAsync(a =>
            a.EmployeeId == employeeId
            && a.EventType == AuditEventType.ScanBlockedOnDevice
            && a.CreatedAtUtc >= since
            && a.Reason != null
            && a.Reason.StartsWith(request.Reason), HttpContext.RequestAborted);

        if (!alreadyLogged)
        {
            // Accuracy rides along as "Code|metres"; the reports layer splits it back off so the
            // per-reason tally still groups on the bare code. Keeps AuditLog's shape unchanged.
            var accuracy = request.AccuracyMeters;
            var reason = accuracy is > 0
                ? $"{request.Reason}|{Math.Round(accuracy.Value)}"
                : request.Reason;

            await WriteAuditAsync(employeeId, AuditEventType.ScanBlockedOnDevice, reason,
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        return Accepted();
    }

    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] ScanRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Identity comes from the authenticated JWT ("sub" claim), never from the body.
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        // 1. QR token validity (signature/format/expiry — all server-side).
        var validation = _qrTokenService.Validate(request.QrToken);
        if (!validation.IsValid)
        {
            await WriteAuditAsync(employeeId, AuditEventType.CheckInRejected, validation.FailureReason, ip);
            return BadRequest(new { error = validation.FailureReason });
        }

        // No per-token replay/nonce check: the QR is a STATIC printed poster meant to be scanned by
        // many employees, repeatedly, all day. A single-use nonce would let only one person check in
        // per ~TTL window and reject everyone else with "TokenReused". Anti-fraud is instead enforced
        // by geofence + device binding + photo audit + QrVersion (admin revoke) + token expiry.

        // 3. Employee must exist and be active.
        var employee = await _db.Employees
            .Include(e => e.DeviceBinding)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
        if (employee is null)
        {
            await WriteAuditAsync(null, AuditEventType.CheckInRejected, "EmployeeNotFoundOrInactive", ip);
            return Unauthorized(new { error = "EmployeeNotFoundOrInactive" });
        }

        // 4. Device binding is now mandatory — every activated employee has a bound device.
        if (employee.DeviceBinding is null)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "NoDeviceBound", ip);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NoDeviceBound" });
        }
        if (!string.Equals(employee.DeviceBinding.DeviceFingerprint, request.DeviceFingerprint, StringComparison.Ordinal))
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "DeviceMismatch", ip);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "DeviceMismatch" });
        }

        // 5. Geofence — must be within the location's radius (token carries the LocationId).
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == validation.LocationId!.Value);
        if (location is null)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "LocationNotFound", ip);
            return BadRequest(new { error = "LocationNotFound" });
        }
        if (!location.IsActive)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "LocationInactive", ip);
            return BadRequest(new { error = "LocationInactive" });
        }
        // A version mismatch means this QR was revoked (admin "invalidated" it after this token was
        // issued — e.g. a printed poster after regeneration) — treat it the same as an expired code.
        if (validation.Version != location.QrVersion)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "TokenExpired", ip);
            return BadRequest(new { error = "TokenExpired" });
        }

        var distanceMeters = GeoCalculator.DistanceMeters(
            request.Latitude, request.Longitude, location.Latitude, location.Longitude);
        if (distanceMeters > location.RadiusMeters)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "OutsideRadius", ip);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "OutsideRadius", distanceMeters = Math.Round(distanceMeters) });
        }

        // 6. Resolve today's record (server UTC day) and decide check-in vs check-out.
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);

        var record = await _db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeId == employee.Id && r.AttendanceDate == today);

        if (record is null)
            return await CheckInAsync(employee, location, today, nowUtc, ip, request.PhotoBase64);

        if (record.CheckOutAtUtc is null)
        {
            // Reject an accidental rapid second scan instead of checking the employee straight back
            // out. A genuine check-out is many minutes/hours later; a scan seconds after check-in is
            // a double-tap ("did it work?"), so keep them checked IN and tell them.
            if (record.CheckInAtUtc is DateTime checkIn
                && nowUtc - checkIn < TimeSpan.FromMinutes(MinCheckoutMinutes))
            {
                await WriteAuditAsync(employee.Id, AuditEventType.CheckOutRejected, "TooSoonToCheckOut", ip);
                return Conflict(new { error = "TooSoonToCheckOut", minutes = MinCheckoutMinutes });
            }
            return await CheckOutAsync(record, nowUtc, employee.Id, ip);
        }

        // Already checked in and out today.
        await WriteAuditAsync(employee.Id, AuditEventType.CheckOutRejected, "AlreadyCompleted", ip);
        return Conflict(new { error = "AlreadyCompleted" });
    }

    private async Task<IActionResult> CheckInAsync(
        Employee employee, Location location, DateOnly today, DateTime nowUtc, string? ip, string? photoBase64)
    {
        var record = new AttendanceRecord
        {
            EmployeeId = employee.Id,
            LocationId = location.Id,
            AttendanceDate = today,
            CheckInAtUtc = nowUtc,
            Status = DetermineStatus(location, nowUtc)
        };
        _db.AttendanceRecords.Add(record);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent check-in for the same (EmployeeId, AttendanceDate) hit the unique
            // index. Detach the failed insert so the audit write below can persist cleanly.
            _db.Entry(record).State = EntityState.Detached;
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "DuplicateCheckIn", ip);
            return Conflict(new { error = "DuplicateCheckIn" });
        }

        // Photo audit — strictly best-effort and AFTER the check-in has been committed, so a storage
        // failure can never block or roll back attendance. Failure just leaves the photo key null.
        await TryStoreCheckInPhotoAsync(employee, record, photoBase64);

        await WriteAuditAsync(employee.Id, AuditEventType.CheckInSuccess, null, ip);
        return Ok(new
        {
            action = "CheckIn",
            recordId = record.Id,
            status = record.Status.ToString(),
            checkInAtUtc = nowUtc,
            photoStored = record.CheckInPhotoKey is not null
        });
    }

    // Decodes and uploads the check-in selfie, then persists the resulting object key. Also seeds the
    // employee's reference selfie the first time one is available (there is no separate enrollment
    // capture yet). Never throws: any failure is logged and swallowed so check-in still succeeds.
    private async Task TryStoreCheckInPhotoAsync(Employee employee, AttendanceRecord record, string? photoBase64)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
            return;

        try
        {
            var bytes = DecodeImage(photoBase64);
            // Sanity bound: the client sends ~30–60 KB WebP. Reject empty or absurdly large payloads.
            if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024)
            {
                _logger.LogWarning(
                    "Photo audit: skipping check-in photo for employee {EmployeeId} (decoded {Bytes} bytes)", employee.Id, bytes.Length);
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var ct = HttpContext.RequestAborted;
            // Whether a reference existed BEFORE this scan — decides if there's anything to face-match
            // against (the very first check-in only seeds the reference, so there's nothing to compare).
            var hadReference = !string.IsNullOrEmpty(employee.ReferencePhotoKey);

            record.CheckInPhotoKey = await _photoStorage.UploadCheckInPhotoAsync(employee.Id, record.Id, bytes, ct);
            record.CheckInPhotoTakenAtUtc = nowUtc;

            // Reference fallback: the first time we ever have a photo for this employee, keep a copy
            // as their reference selfie for the manager to compare future check-ins against.
            if (!hadReference)
            {
                employee.ReferencePhotoKey = await _photoStorage.UploadReferencePhotoAsync(employee.Id, bytes, ct);
                employee.ReferencePhotoTakenAtUtc = nowUtc;
            }

            await _db.SaveChangesAsync(ct);

            // Queue a background face-match only when there's a prior reference to compare against.
            if (hadReference)
                _faceQueue.Enqueue(record.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Photo audit: failed to store check-in photo for employee {EmployeeId}, record {RecordId}", employee.Id, record.Id);
        }
    }

    // Accepts a data URL ("data:image/webp;base64,AAAA…") or a bare base64 string.
    private static byte[] DecodeImage(string input)
    {
        var comma = input.IndexOf(',');
        var b64 = input.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? input[(comma + 1)..]
            : input;
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    private async Task<IActionResult> CheckOutAsync(
        AttendanceRecord record, DateTime nowUtc, Guid employeeId, string? ip)
    {
        record.CheckOutAtUtc = nowUtc;
        await _db.SaveChangesAsync();

        await WriteAuditAsync(employeeId, AuditEventType.CheckOutSuccess, null, ip);
        return Ok(new
        {
            action = "CheckOut",
            recordId = record.Id,
            checkOutAtUtc = nowUtc
        });
    }

    /// <summary>
    /// OnTime unless the current time is past ShiftStart + LateThresholdMinutes.
    /// Note: shift times and server time are treated as the same reference here; a real
    /// deployment would carry a per-location timezone.
    /// Internal (not private) so AdminAttendanceController can recompute the same way when an
    /// admin edits/creates a record's check-in time.
    /// </summary>
    internal static AttendanceStatus DetermineStatus(Location location, DateTime nowUtc)
    {
        var lateCutoff = location.ShiftStart.AddMinutes(location.LateThresholdMinutes);
        var nowTime = TimeOnly.FromDateTime(nowUtc);
        return nowTime > lateCutoff ? AttendanceStatus.Late : AttendanceStatus.OnTime;
    }

    private async Task WriteAuditAsync(Guid? employeeId, AuditEventType eventType, string? reason, string? ip)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = eventType,
            Reason = reason,
            IpAddress = ip
        });
        await _db.SaveChangesAsync();
    }
}
