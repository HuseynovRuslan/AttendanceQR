using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Application.Common;
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
    private readonly DeviceBindingOptions _deviceOptions;
    private readonly TimeZoneInfo _timeZone;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(
        AppDbContext db,
        IQrTokenService qrTokenService,
        IAttendanceQueryService attendanceQuery,
        IPhotoStorageService photoStorage,
        IFaceMatchQueue faceQueue,
        DeviceBindingOptions deviceOptions,
        AppOptions appOptions,
        ILogger<AttendanceController> logger)
    {
        _db = db;
        _qrTokenService = qrTokenService;
        _attendanceQuery = attendanceQuery;
        _photoStorage = photoStorage;
        _faceQueue = faceQueue;
        _deviceOptions = deviceOptions;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
        _logger = logger;
    }

    // GET /api/attendance/me — the caller's own records. Identity is the JWT "sub"; there is no
    // way to ask for anyone else's here.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var employeeId = User.EmployeeId();

        var records = await _attendanceQuery.GetOwnRecordsAsync(employeeId, HttpContext.RequestAborted);
        return Ok(records);
    }

    // GET /api/attendance/me/profile — the caller's own profile (name/location) for the mobile
    // home greeting + menu card. The JWT only carries id/email/role, so name comes from here.
    [HttpGet("me/profile")]
    public async Task<IActionResult> MyProfile()
    {
        var employeeId = User.EmployeeId();

        // Record "Son aktivlik" — the mobile home/menu load this on open, so it's our signal for
        // "opened the app". Throttled to once per 15 min so a refresh-happy client doesn't write on
        // every load; best-effort, and it never blocks the profile response.
        var now = DateTime.UtcNow;
        var activityCutoff = now.AddMinutes(-15);
        await _db.Employees
            .Where(e => e.Id == employeeId && (e.LastActiveAtUtc == null || e.LastActiveAtUtc < activityCutoff))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastActiveAtUtc, now), HttpContext.RequestAborted);

        var profile = await _db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => new
            {
                fullName = e.FullName,
                email = e.Email,
                role = e.Role.ToString(),
                position = e.Position,
                // For the home-screen birthday greeting; the client compares day/month to today.
                birthDate = e.BirthDate,
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

    // POST /api/attendance/me/reference-photo — the caller sets their OWN reference selfie (the
    // face-audit baseline). Used by the first-login flow for temp-PIN accounts, which never took an
    // activation selfie; overwrites any existing reference.
    [HttpPost("me/reference-photo")]
    public async Task<IActionResult> SetMyReferencePhoto([FromBody] ReferencePhotoRequest request)
    {
        var employeeId = User.EmployeeId();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, HttpContext.RequestAborted);
        if (employee is null)
            return Unauthorized(new { error = "InvalidToken" });

        var bytes = DecodeImage(request.PhotoBase64);
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024)
            return BadRequest(new { error = "InvalidPhoto" });

        var ct = HttpContext.RequestAborted;
        employee.ReferencePhotoKey = await _photoStorage.UploadReferencePhotoAsync(employee.Id, bytes, ct);
        employee.ReferencePhotoTakenAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    // GET /api/attendance/me/device?fingerprint=… — is the browser asking bound to the caller's own
    // account? Answers the one question an employee cannot otherwise find out except by walking to
    // the poster and failing: "will this phone/app actually work tomorrow morning?"
    [HttpGet("me/device")]
    public async Task<IActionResult> MyDevice([FromQuery] string? fingerprint)
    {
        var employeeId = User.EmployeeId();

        var bindings = await _db.DeviceBindings
            .Where(d => d.EmployeeId == employeeId)
            .ToListAsync(HttpContext.RequestAborted);

        var mine = bindings.FirstOrDefault(d =>
            string.Equals(d.DeviceFingerprint, fingerprint, StringComparison.Ordinal));

        // The employee's assigned location, so the scan page can pre-check the geofence (show "you're
        // at the workplace / X m away" BEFORE scanning). The scan itself still checks against the QR's
        // own location server-side — this is a pre-check against where the employee is expected to be.
        var location = await _db.Employees
            .Where(e => e.Id == employeeId)
            .Join(_db.Locations, e => e.LocationId, l => l.Id,
                (e, l) => new { l.Name, l.Latitude, l.Longitude, l.RadiusMeters })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        return Ok(new
        {
            bound = mine is { IsActive: true },
            // Revoked by an admin: no scan will adopt it back, so the employee must ask rather than
            // stand at the poster wondering. Distinct from simply never having been bound.
            revoked = mine?.RevokedAtUtc != null,
            deviceLabel = mine?.DeviceLabel,
            boundAtUtc = mine is { IsActive: true } ? mine.BoundAtUtc : (DateTime?)null,
            activeDeviceCount = bindings.Count(d => d.IsActive),
            // Nothing to adopt an unknown device with while this is off — the app says so plainly.
            autoBindEnabled = _deviceOptions.AutoBind,
            location = location is null
                ? null
                : new
                {
                    name = location.Name,
                    latitude = location.Latitude,
                    longitude = location.Longitude,
                    radiusMeters = location.RadiusMeters
                }
        });
    }

    // GET /api/attendance/employee/{id} — another employee's records, subject to a resource-level
    // check in the service ([Authorize] alone cannot enforce "only your own / your team's").
    [HttpGet("employee/{employeeId:guid}")]
    public async Task<IActionResult> ForEmployee(Guid employeeId)
    {
        var requesterId = User.EmployeeId();

        var role = User.Role();

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
        var requesterId = User.EmployeeId();
        var role = User.Role();

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
        var employeeId = User.EmployeeId();

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
        var employeeId = User.EmployeeId();

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
            .Include(e => e.DeviceBindings)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
        if (employee is null)
        {
            await WriteAuditAsync(null, AuditEventType.CheckInRejected, "EmployeeNotFoundOrInactive", ip);
            return Unauthorized(new { error = "EmployeeNotFoundOrInactive" });
        }

        // Idempotency: a replayed offline scan (re-sent from the queue, or a scan whose response was
        // lost) carries the same client id it was first sent with. If we've already processed it, don't
        // create a second check-in/out — tell the client it's already recorded so it drops the queue item.
        if (request.ClientScanId is Guid seenScanId
            && await _db.ProcessedScans.AnyAsync(p => p.ClientScanId == seenScanId))
        {
            return Ok(new { action = "AlreadyRecorded", alreadyProcessed = true });
        }

        // 4. Geofence — must be within the location's radius (token carries the LocationId).
        //    Checked BEFORE the device on purpose: an unrecognised device may only be adopted once we
        //    know the employee is standing at the location, so nobody can bind a phone from home.
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

        // 5. Device. The fingerprint identifies a BROWSER STORAGE CONTEXT, not a phone — Safari and
        //    the installed PWA are separate contexts on the same handset and the web offers no way to
        //    link them. So the employee holds several bindings, and an unknown one arriving from
        //    inside the geofence is adopted rather than rejected (while AutoBind is on).
        var deviceRejection = await ResolveDeviceAsync(employee, request.DeviceFingerprint, ip);
        if (deviceRejection is not null)
            return deviceRejection;

        // 6. Resolve today's record (server UTC day) and decide check-in vs check-out.
        // An offline scan carries the phone's clock; trust it only within a sane window, otherwise fall
        // back to server time so a rolled-back clock can't fake an on-time arrival. Online scans (the
        // overwhelming majority) always use server time — Offline is false, so this is a no-op for them.
        var serverNow = DateTime.UtcNow;
        var nowUtc = serverNow;
        if (request.Offline && request.ClientTimestampUtc is DateTime clientTs)
        {
            var clientUtc = DateTime.SpecifyKind(clientTs, DateTimeKind.Utc);
            if (clientUtc <= serverNow.AddMinutes(10) && clientUtc >= serverNow.AddHours(-18))
                nowUtc = clientUtc;
        }
        var today = DateOnly.FromDateTime(nowUtc);

        var record = await _db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeId == employee.Id && r.AttendanceDate == today);

        if (record is null)
        {
            // Night shift: a MORNING scan is the check-OUT of a shift that began the previous evening
            // and crossed midnight. There is no record for "today" yet, so without this the scan would
            // wrongly open a fresh check-in and leave last night's shift forever un-closed. Strictly
            // additive — the branch only runs for an overnight shift (end earlier than start) scanned
            // before noon, so ordinary day shifts are completely unaffected.
            var shiftStart = EffectiveShiftStart(employee, location);
            var shiftEnd = EffectiveShiftEnd(employee, location);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);
            if (shiftEnd < shiftStart && nowLocal.Hour < 12)
            {
                var yesterday = today.AddDays(-1);
                var openNight = await _db.AttendanceRecords.FirstOrDefaultAsync(r =>
                    r.EmployeeId == employee.Id && r.AttendanceDate == yesterday
                    && r.CheckInAtUtc != null && r.CheckOutAtUtc == null);
                if (openNight is not null)
                {
                    if (openNight.CheckInAtUtc is DateTime nightIn
                        && nowUtc - nightIn < TimeSpan.FromMinutes(MinCheckoutMinutes))
                    {
                        await WriteAuditAsync(employee.Id, AuditEventType.CheckOutRejected, "TooSoonToCheckOut", ip);
                        return Conflict(new { error = "TooSoonToCheckOut", minutes = MinCheckoutMinutes });
                    }
                    return await CheckOutAsync(openNight, employee, location, nowUtc, ip,
                        request.ClientScanId, request.Offline, serverNow);
                }
            }

            return await CheckInAsync(employee, location, today, nowUtc, ip, request.PhotoBase64,
                request.ClientScanId, request.Offline, serverNow);
        }

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
            return await CheckOutAsync(record, employee, location, nowUtc, ip,
                request.ClientScanId, request.Offline, serverNow);
        }

        // Already checked in and out today.
        await WriteAuditAsync(employee.Id, AuditEventType.CheckOutRejected, "AlreadyCompleted", ip);
        return Conflict(new { error = "AlreadyCompleted" });
    }

    private async Task<IActionResult> CheckInAsync(
        Employee employee, Location location, DateOnly today, DateTime nowUtc, string? ip, string? photoBase64,
        Guid? clientScanId = null, bool wasOffline = false, DateTime? submittedAtUtc = null)
    {
        var record = new AttendanceRecord
        {
            EmployeeId = employee.Id,
            LocationId = location.Id,
            AttendanceDate = today,
            CheckInAtUtc = nowUtc,
            Status = DetermineStatus(EffectiveShiftStart(employee, location), location.LateThresholdMinutes, nowUtc, _timeZone),
            WasOffline = wasOffline,
            SubmittedAtUtc = wasOffline ? submittedAtUtc : null,
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

        // Mark this scan processed so a replay of the same offline queue item doesn't check in twice.
        await RecordProcessedScanAsync(clientScanId, employee.Id);

        // Photo audit — strictly best-effort and AFTER the check-in has been committed, so a storage
        // failure can never block or roll back attendance. Failure just leaves the photo key null.
        await TryStoreCheckInPhotoAsync(employee, record, photoBase64);

        await WriteAuditAsync(employee.Id, AuditEventType.CheckInSuccess, null, ip);

        // How many PAST days this employee left open (checked in, never out). Those days count as zero
        // hours, so the check-in card can show the running cost — the nudge that breaks the habit
        // without auto-closing anything or asking for a reason.
        var openDays = await _db.AttendanceRecords.CountAsync(r =>
            r.EmployeeId == employee.Id && r.AttendanceDate < today
            && r.CheckInAtUtc != null && r.CheckOutAtUtc == null);

        return Ok(new
        {
            action = "CheckIn",
            recordId = record.Id,
            status = record.Status.ToString(),
            // Tells the app to prompt for a late-arrival reason (skippable). Uses the employee's own
            // hours when set (EffectiveShiftStart).
            late = record.Status == AttendanceStatus.Late,
            checkInAtUtc = nowUtc,
            photoStored = record.CheckInPhotoKey is not null,
            openDays
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
            // The worker has no request to resolve a tenant from, so hand it the one this record was
            // just written under.
            if (hadReference)
                _faceQueue.Enqueue(_db.CurrentTenantId, record.Id);
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
        AttendanceRecord record, Employee employee, Location location, DateTime nowUtc, string? ip,
        Guid? clientScanId = null, bool wasOffline = false, DateTime? submittedAtUtc = null)
    {
        record.CheckOutAtUtc = nowUtc;
        // OR-in the offline flag: a record is "offline" if EITHER its check-in or check-out was.
        if (wasOffline)
        {
            record.WasOffline = true;
            record.SubmittedAtUtc = submittedAtUtc;
        }
        await _db.SaveChangesAsync();
        await RecordProcessedScanAsync(clientScanId, employee.Id);

        await WriteAuditAsync(employee.Id, AuditEventType.CheckOutSuccess, null, ip);
        return Ok(new
        {
            action = "CheckOut",
            recordId = record.Id,
            checkOutAtUtc = nowUtc,
            // Tells the app to prompt for an early-departure reason (skippable).
            earlyDeparture = IsEarlyDeparture(EffectiveShiftEnd(employee, location), location.LateThresholdMinutes, nowUtc, _timeZone)
        });
    }

    // Records the idempotency marker for a scan that carried a client id. Best-effort and isolated from
    // the check-in/out it follows: on a unique-index race (the same offline item sent twice at once) the
    // duplicate is detached and swallowed, since the record it protects is already committed.
    private async Task RecordProcessedScanAsync(Guid? clientScanId, Guid employeeId)
    {
        if (clientScanId is not Guid id)
            return;

        var entry = _db.ProcessedScans.Add(new ProcessedScan { ClientScanId = id, EmployeeId = employeeId });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            entry.State = EntityState.Detached;
        }
    }

    // The effective shift bounds for an employee: their own WorkStart/WorkEnd when set, else the
    // location's. Staff at one location can keep different hours (the reason "Gecikmə" was dropped as a
    // location-wide label) — this makes late/early detection per-person when needed.
    internal static TimeOnly EffectiveShiftStart(Employee employee, Location location)
        => employee.WorkStart ?? location.ShiftStart;

    internal static TimeOnly EffectiveShiftEnd(Employee employee, Location location)
        => employee.WorkEnd ?? location.ShiftEnd;

    /// <summary>
    /// OnTime unless the current time is past shiftStart + lateThreshold. shiftStart is the employee's
    /// own WorkStart when set, else the location's ShiftStart (see EffectiveShiftStart).
    /// Note: shift times and server time are treated as the same reference here; a real
    /// deployment would carry a per-location timezone.
    /// Internal (not private) so AdminAttendanceController can recompute the same way when an
    /// admin edits/creates a record's check-in time.
    /// </summary>
    internal static AttendanceStatus DetermineStatus(TimeOnly shiftStart, int lateThresholdMinutes, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        var nowLocal = LocalTimeOfDay(nowUtc, timeZone);
        return nowLocal > shiftStart.AddMinutes(lateThresholdMinutes) ? AttendanceStatus.Late : AttendanceStatus.OnTime;
    }

    /// <summary>True when the check-out is more than lateThreshold minutes before shiftEnd — the same
    /// grace as late arrival, applied to early departure.</summary>
    internal static bool IsEarlyDeparture(TimeOnly shiftEnd, int lateThresholdMinutes, DateTime nowUtc, TimeZoneInfo timeZone)
        => LocalTimeOfDay(nowUtc, timeZone) < shiftEnd.AddMinutes(-lateThresholdMinutes);

    // Shift times are stored as LOCAL wall-clock (Asia/Baku = UTC+4); the scan time is UTC. Convert
    // before comparing — otherwise a 15:00Z (= 19:00 local) check-out reads as "before 18:00" and is
    // wrongly flagged early. This was the bug that asked Ənvər why he left early at 19:00.
    internal static TimeOnly LocalTimeOfDay(DateTime nowUtc, TimeZoneInfo timeZone)
        => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timeZone));

    // Decides whether this device may scan, adopting it if the rules allow. Returns null when the
    // scan may proceed, or the rejection to send back. Callers MUST have passed the geofence first —
    // being physically at the location is the whole evidence behind an automatic binding.
    private async Task<IActionResult?> ResolveDeviceAsync(Employee employee, string fingerprint, string? ip)
    {
        var nowUtc = DateTime.UtcNow;

        var known = employee.DeviceBindings.FirstOrDefault(d =>
            d.IsActive && string.Equals(d.DeviceFingerprint, fingerprint, StringComparison.Ordinal));
        if (known is not null)
        {
            known.LastSeenAtUtc = nowUtc;   // keeps this context out of the eviction queue
            await _db.SaveChangesAsync();
            return null;
        }

        // An admin killed this context. Re-adopting it on the next scan would make "revoke" a no-op,
        // so it stays dead until an admin approves a device-change request for it.
        var revoked = employee.DeviceBindings.FirstOrDefault(d =>
            d.RevokedAtUtc != null && string.Equals(d.DeviceFingerprint, fingerprint, StringComparison.Ordinal));
        if (revoked is not null)
            return await RejectDeviceAsync(employee, ip);

        // Strict mode: the pre-rollout behaviour, one binding and an admin approves any change.
        if (!_deviceOptions.AutoBind)
            return await RejectDeviceAsync(employee, ip);

        // Private browsing hands out a fresh storage context per session — uncapped, it would mint a
        // binding on every scan. Hitting this limit means "talk to this employee", not "attack".
        var since = nowUtc.AddDays(-30);
        var recentBinds = await _db.AuditLogs.CountAsync(a =>
            a.EmployeeId == employee.Id
            && a.EventType == AuditEventType.DeviceAutoBound
            && a.CreatedAtUtc >= since);
        if (recentBinds >= _deviceOptions.MaxBindsPer30Days)
            return await RejectDeviceAsync(employee, ip);

        var binding = DeviceBindingRules.Bind(
            employee.DeviceBindings.ToList(),
            employee.Id,
            fingerprint,
            DeviceLabels.FromUserAgent(Request.Headers.UserAgent.ToString()),
            DeviceBindingOrigin.AutoBind,
            _deviceOptions.MaxActiveDevices,
            nowUtc);

        // Must go through the DbSet, NOT employee.DeviceBindings.Add(). The DeviceBinding constructor
        // assigns its own Guid, so a new entity discovered through a navigation property looks to
        // change-tracking like an existing row (key already set) — EF then issues an UPDATE that
        // matches nothing, throws DbUpdateConcurrencyException, and the scan dies as a "network
        // error" on the phone. DbSet.Add marks it Added explicitly.
        if (!employee.DeviceBindings.Contains(binding))
            _db.DeviceBindings.Add(binding);

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employee.Id,
            EventType = AuditEventType.DeviceAutoBound,
            Reason = binding.DeviceLabel,
            IpAddress = ip
        });
        await _db.SaveChangesAsync();

        _logger.LogInformation("Auto-bound device for employee {EmployeeId} ({Label})", employee.Id, binding.DeviceLabel);
        return null;
    }

    private async Task<IActionResult> RejectDeviceAsync(Employee employee, string? ip)
    {
        // "No device at all" and "the wrong device" send the employee down different paths in the
        // app — the first is an admin problem, the second offers "this is my new phone".
        var reason = employee.DeviceBindings.Any(d => d.IsActive) ? "DeviceMismatch" : "NoDeviceBound";
        await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, reason, ip);
        return StatusCode(StatusCodes.Status403Forbidden, new { error = reason });
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
