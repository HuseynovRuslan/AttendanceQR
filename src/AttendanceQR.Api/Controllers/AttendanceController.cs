using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Application.Common;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

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
        ["GpsPermissionDenied", "GpsUnavailable", "GpsTimeout", "GpsUnsupported", "GpsInaccurate",
         // The scan failed on the phone for a non-GPS reason too — so a stuck employee is visible on
         // the Problems screen instead of only phoning in. CameraBlocked = the camera would not open;
         // NetworkError = the request never reached us (reported later, once the phone reconnects).
         "CameraBlocked", "NetworkError", "ScanError",
         // An offline scan that never became a record. OfflineRejected = the replay was refused by a
         // definitive 4xx; OfflineExpired = it sat past the window in which the phone's clock is still
         // trusted, so replaying it would have written the wrong DAY. Both used to be a silent delete
         // on the device: the employee saw "saved", the tabel said Qayıb, and nobody could connect
         // the two. These put the event where an admin can act on it.
         "OfflineRejected", "OfflineExpired"];

    // A blocked employee retries over and over. Collapse the same (employee, reason) into one
    // incident for this long, so one stuck phone doesn't bury the day's real problems.
    private static readonly TimeSpan FailureDedupeWindow = TimeSpan.FromMinutes(5);

    // Every photo-check is a paid AWS Rekognition call, and the endpoint is [Authorize]-only with no
    // ceiling — any signed-in employee could loop it and run up the bill (or exhaust the quota for
    // everyone else). A generous per-employee hourly budget bounds both: a real person scans twice a
    // day and might retake a photo once or twice, so 20 is far above normal use and far below abuse.
    /// <summary>
    /// How far back an OFFLINE scan's own timestamp is still trusted. The client mirrors this in
    /// <c>MAX_QUEUED_AGE_MS</c> as a pre-filter, but this is the authority: past it the scan is
    /// refused, never silently re-dated.
    /// </summary>
    public const int OfflineTrustWindowHours = 18;

    private const int MaxFaceChecksPerHour = 20;
    private static readonly TimeSpan FaceCheckWindow = TimeSpan.FromHours(1);

    private readonly AppDbContext _db;
    private readonly IQrTokenService _qrTokenService;
    private readonly IAttendanceQueryService _attendanceQuery;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IFaceMatchQueue _faceQueue;
    private readonly IPhotoUploadQueue _photoQueue;
    private readonly IFaceMatchService _faceMatch;
    private readonly DeviceBindingOptions _deviceOptions;
    private readonly TimeZoneInfo _timeZone;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AttendanceController> _logger;

    public AttendanceController(
        AppDbContext db,
        IQrTokenService qrTokenService,
        IAttendanceQueryService attendanceQuery,
        IPhotoStorageService photoStorage,
        IFaceMatchQueue faceQueue,
        IPhotoUploadQueue photoQueue,
        IFaceMatchService faceMatch,
        DeviceBindingOptions deviceOptions,
        AppOptions appOptions,
        IMemoryCache cache,
        ILogger<AttendanceController> logger)
    {
        _db = db;
        _qrTokenService = qrTokenService;
        _attendanceQuery = attendanceQuery;
        _photoStorage = photoStorage;
        _faceQueue = faceQueue;
        _photoQueue = photoQueue;
        _faceMatch = faceMatch;
        _deviceOptions = deviceOptions;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
        _cache = cache;
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

    // GET /api/attendance/me/today — just the caller's record for today (or null). The Scan page waits
    // on THIS before opening the camera, so it must not pull the whole history — one indexed row only.
    [HttpGet("me/today")]
    public async Task<IActionResult> MyToday()
    {
        var employeeId = User.EmployeeId();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        var record = await _attendanceQuery.GetTodayAsync(employeeId, today, HttpContext.RequestAborted);
        return Ok(record); // null when there's no scan yet today
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
                // The scan screen skips the selfie entirely for an exempted employee — showing a
                // camera it will then discard would just teach them the step is optional.
                photoRequired = !e.PhotoExempt,
                // Gates the field/mobile check-in surfaces in the app (menu row, home self-report).
                canFieldCheckIn = e.CanFieldCheckIn,
                // The app blocks on the consent screen until this is accepted.
                consentRequired = e.ConsentAcceptedAtUtc == null,
                e.LocationId, e.ScheduleId,
                e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor,
                locationName = _db.Locations
                    .Where(l => l.Id == e.LocationId)
                    .Select(l => l.Name)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (profile is null)
            return NotFound(new { error = "NotFound" });

        // The employee's effective shift END, so the home screen can escalate a still-open check-in to
        // "your shift is over, don't forget to check out" — the mobile side of the forgotten-checkout
        // problem. Resolved the same way the scan and reports do; null if the location vanished.
        string? shiftEnd = null, shiftStart = null;
        var loc = await _db.Locations.FirstOrDefaultAsync(l => l.Id == profile.LocationId, HttpContext.RequestAborted);
        if (loc is not null)
        {
            var sched = profile.ScheduleId is Guid schId
                ? await _db.Schedules.FirstOrDefaultAsync(s => s.Id == schId, HttpContext.RequestAborted)
                : null;
            var shift = EffectiveShift.Resolve(
                profile.WorkStart, profile.WorkEnd, profile.WorkCycleDays, profile.WorkCycleOnDays,
                profile.WorkCycleAnchor, sched, loc);
            shiftStart = shift.Start.ToString("HH:mm");
            shiftEnd = shift.End.ToString("HH:mm");
        }

        // Check-ins this month whose photo showed no face. Told to the employee themselves, not only
        // to an auditor: people correct a habit they can see a count of, long before anyone has to
        // raise it with them.
        var monthStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1 - DateTime.UtcNow.Day);
        var unverified = await _db.AttendanceRecords
            .CountAsync(r => r.EmployeeId == employeeId && r.AttendanceDate >= monthStart
                             && r.FaceMatchStatus == FaceMatchStatus.NoFace, HttpContext.RequestAborted);

        // Whether the LAST check-in was one of them — the scan screen warns before the camera opens,
        // which is the only moment the warning can still change what they do.
        var lastWasUnverified = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.CheckInAtUtc != null)
            .OrderByDescending(r => r.AttendanceDate)
            .Select(r => r.FaceMatchStatus)
            .FirstOrDefaultAsync(HttpContext.RequestAborted) == FaceMatchStatus.NoFace;

        return Ok(new
        {
            profile.fullName, profile.email, profile.role, profile.position, profile.birthDate,
            profile.photoRequired, profile.consentRequired, profile.locationName,
            profile.canFieldCheckIn,
            shiftStart, shiftEnd,
            unverifiedCheckIns = unverified,
            lastCheckInUnverified = lastWasUnverified,
        });
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

    // POST /api/attendance/me/consent — the employee agrees to the data-processing notice. Idempotent:
    // the first acceptance stamps the time; later calls leave it untouched (the original consent stands).
    [HttpPost("me/consent")]
    public async Task<IActionResult> AcceptConsent()
    {
        var employeeId = User.EmployeeId();
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, HttpContext.RequestAborted);
        if (employee is null)
            return Unauthorized(new { error = "EmployeeNotFound" });

        employee.ConsentAcceptedAtUtc ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { acceptedAtUtc = employee.ConsentAcceptedAtUtc });
    }

    // POST /api/attendance/me/photo-check — "is there a face in this photo?", asked from the scan
    // screen BEFORE the check-in is submitted.
    //
    // This lives on the server because the browsers people actually use cannot answer it: Chrome's
    // FaceDetector is behind an experimental flag and Safari has nothing at all, so an on-device
    // check is silent on every real phone. Rekognition already runs here for the audit; this is the
    // same detection asked a few seconds earlier, when the employee can still retake the photo.
    //
    // Deliberately advisory: it records nothing, decides nothing, and answers -1 whenever it cannot
    // tell. The check-in that follows is submitted either way.
    [HttpPost("me/photo-check")]
    public async Task<IActionResult> PhotoCheck([FromBody] ReferencePhotoRequest request)
    {
        // Out of budget answers the SAME -1 this endpoint already returns whenever it cannot tell, so
        // the cap can never become a new failure mode: the employee just loses the retake hint, and
        // the check-in that follows was never gated on this.
        var budgetKey = $"facecheck:{User.EmployeeId()}";
        var used = _cache.TryGetValue(budgetKey, out int n) ? n : 0;
        if (used >= MaxFaceChecksPerHour)
            return Ok(new { faces = -1 });

        var bytes = DecodeImage(request.PhotoBase64);
        if (bytes.Length is <= 0 or > 2 * 1024 * 1024)
            return Ok(new { faces = -1 });

        // Counted immediately before the paid call, so a call that then fails still spends budget —
        // otherwise forcing failures would be a free loop.
        _cache.Set(budgetKey, used + 1, FaceCheckWindow);
        var faces = await _faceMatch.DetectFaceCountAsync(bytes, HttpContext.RequestAborted);
        return Ok(new { faces });
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

        // Detail rides along as "Code|detail"; the reports layer splits it back off so the per-reason
        // tally still groups on the bare code. Built HERE, never accepted pre-joined, because the
        // allow-list above matches the whole Reason string.
        //   • a coarse position → the metres
        //   • an offline scan reported later → the DAY it was taken, which is the only thing that
        //     makes the row actionable (the admin has to enter that day by hand)
        var scanDay = request.ScanAtUtc is DateTime taken
            ? DateOnly.FromDateTime(DateTime.SpecifyKind(taken, DateTimeKind.Utc)).ToString("yyyy-MM-dd")
            : null;
        var detail = scanDay ?? (request.AccuracyMeters is > 0
            ? Math.Round(request.AccuracyMeters.Value).ToString()
            : null);
        var reason = detail is null ? request.Reason : $"{request.Reason}|{detail}";

        // Collapse a retrying phone into one incident — but on the FULL reason, so two different lost
        // DAYS stay two rows. A drain loop emits its reports in the same second, so deduping on the
        // bare code alone turned four lost days into one line and hid three of them.
        var since = DateTime.UtcNow - FailureDedupeWindow;
        var alreadyLogged = await _db.AuditLogs.AnyAsync(a =>
            a.EmployeeId == employeeId
            && a.EventType == AuditEventType.ScanBlockedOnDevice
            && a.CreatedAtUtc >= since
            && a.Reason != null
            && a.Reason == reason, HttpContext.RequestAborted);

        if (!alreadyLogged)
        {
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
            // Record WHERE the scan came from, not just that it was outside. A repeated OutsideRadius
            // at one site is almost always the geofence, not the employee — the poster is beyond the
            // radius, or the centre is on the wrong spot — and that is only diagnosable if the rejected
            // points can be drawn on a map. Stashed in the reason as "OutsideRadius|lat,lng,dist"
            // (invariant '.' decimals; the Problems screen already splits detail off the first '|').
            var geo = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"OutsideRadius|{request.Latitude:F5},{request.Longitude:F5},{Math.Round(distanceMeters)}");
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, geo, ip);
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
            // Outside the window this used to fall back to SERVER time in silence — which does not
            // record the scan late, it records it on the WRONG DAY, and if the employee has already
            // checked in today it is then read as their check-out and closes a live shift. Refuse it
            // instead: the phone drops it down its definitive-4xx path, which reports the lost day to
            // the Problems screen and warns the employee. The client keeps its own age pre-filter, but
            // only ONE side decides — the client cannot know this server's clock, and a phone running
            // slow would otherwise sit in a band where it believes an item is fresh and we disagree.
            if (clientUtc > serverNow.AddMinutes(10) || clientUtc < serverNow.AddHours(-OfflineTrustWindowHours))
            {
                await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected,
                    $"OfflineTooOld|{clientUtc:yyyy-MM-dd}", ip);
                return BadRequest(new { error = "OfflineTooOld" });
            }
            nowUtc = clientUtc;
        }
        var today = DateOnly.FromDateTime(nowUtc);

        // Resolved once here and carried through both branches, so a single scan cannot judge its
        // check-in against one set of hours and its check-out against another.
        var shift = await ResolveShiftAsync(employee, location);

        var record = await _db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeId == employee.Id && r.AttendanceDate == today);

        if (record is null)
        {
            // Night shift: a MORNING scan is the check-OUT of a shift that began the previous evening
            // and crossed midnight. There is no record for "today" yet, so without this the scan would
            // wrongly open a fresh check-in and leave last night's shift forever un-closed. Strictly
            // additive — the branch only runs for an overnight shift (end earlier than start) scanned
            // before noon, so ordinary day shifts are completely unaffected.
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);
            if (shift.IsOvernight && nowLocal.Hour < 12)
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
                    return await CheckOutAsync(openNight, employee, location, shift, nowUtc, ip,
                        request.ClientScanId, request.Offline, serverNow);
                }
            }

            // A scan within the check-out cool-off of a RECENT check-out is the mirror of the double-tap
            // the check-out path already rejects — but here, finding no open record, it would instead
            // open a brand-new shift. Offline sync makes this real: two morning scans arrive in one
            // batch, the first checks the night shift out, and without this the second creates a stray,
            // never-closed check-in (which then reads as "Çıxış yoxdur" and fouls that night's shift).
            // Their real attendance — the check-out — is already recorded, so nothing is lost. Absolute
            // difference because an offline batch can process the two out of client-time order.
            var lastCheckOutUtc = await _db.AttendanceRecords
                .Where(r => r.EmployeeId == employee.Id && r.CheckOutAtUtc != null)
                .OrderByDescending(r => r.CheckOutAtUtc)
                .Select(r => r.CheckOutAtUtc)
                .FirstOrDefaultAsync();
            if (lastCheckOutUtc is DateTime co && (nowUtc - co).Duration() < TimeSpan.FromMinutes(MinCheckoutMinutes))
            {
                await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "TooSoonAfterCheckOut", ip);
                return Conflict(new { error = "AlreadyCompleted" });
            }

            return await CheckInAsync(employee, location, shift, today, nowUtc, ip, request.PhotoBase64,
                request.Latitude, request.Longitude, request.ClientScanId, request.Offline, serverNow);
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
            return await CheckOutAsync(record, employee, location, shift, nowUtc, ip,
                request.ClientScanId, request.Offline, serverNow);
        }

        // Already checked in and out today.
        await WriteAuditAsync(employee.Id, AuditEventType.CheckOutRejected, "AlreadyCompleted", ip);
        return Conflict(new { error = "AlreadyCompleted" });
    }

    private async Task<IActionResult> CheckInAsync(
        Employee employee, Location location, EffectiveShift shift, DateOnly today, DateTime nowUtc, string? ip, string? photoBase64,
        double latitude, double longitude, Guid? clientScanId = null, bool wasOffline = false, DateTime? submittedAtUtc = null)
    {
        var record = new AttendanceRecord
        {
            EmployeeId = employee.Id,
            LocationId = location.Id,
            AttendanceDate = today,
            CheckInAtUtc = nowUtc,
            Status = DetermineStatus(shift.Start, shift.LateThresholdMinutes, nowUtc, _timeZone),
            WasOffline = wasOffline,
            SubmittedAtUtc = wasOffline ? submittedAtUtc : null,
            // The position their scan passed the geofence with — for the dashboard map.
            CheckInLatitude = latitude,
            CheckInLongitude = longitude,
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
        // failure can never block or roll back attendance. The photo is persisted to the DURABLE
        // queue here (one insert); the R2 upload itself runs in PhotoUploadWorker with retries,
        // because 2000 phones at shift start must never wait on object storage for a scan that is
        // already committed — and a deploy or crash must never lose an accepted selfie.
        var photoAccepted = await TryQueueCheckInPhotoAsync(employee, record, photoBase64);

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
            // "Accepted for upload" — the actual R2 write happens out-of-band moments later.
            photoStored = photoAccepted,
            openDays
        });
    }

    // How many photos may sit in the durable queue at once before new ones are refused: ~180MB of
    // bytea at the observed 45KB/photo — a full 2000-employee morning through an outage fits twice
    // over, and the cap is what keeps a week-long outage from eating the disk.
    private const int MaxPendingPhotos = 4000;

    // Validates the check-in selfie and persists it to the DURABLE upload queue (PendingPhotoUploads);
    // PhotoUploadWorker uploads it with retry/backoff, seeds the reference selfie when none exists
    // yet, and queues the face match. Never throws: any failure is logged and swallowed so check-in
    // still succeeds. Returns whether the photo was accepted (= safely persisted, not yet uploaded).
    private async Task<bool> TryQueueCheckInPhotoAsync(Employee employee, AttendanceRecord record, string? photoBase64)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
            return false;

        try
        {
            var bytes = DecodeImage(photoBase64);
            // Sanity bound: the client sends ~30–60 KB WebP. Reject empty or absurdly large payloads.
            if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024)
            {
                _logger.LogWarning(
                    "Photo audit: skipping check-in photo for employee {EmployeeId} (decoded {Bytes} bytes)", employee.Id, bytes.Length);
                return false;
            }

            // Disk guard: past the cap the NEW photo is refused (counted + logged, never silent).
            // PendingApprox is -1 until the worker's first reconcile — treated as "no data, allow".
            if (_photoQueue.PendingApprox >= MaxPendingPhotos)
            {
                _photoQueue.MarkDropped();
                _logger.LogWarning(
                    "Photo audit: durable queue at cap ({Cap}), dropping check-in photo for record {RecordId}",
                    MaxPendingPhotos, record.Id);
                return false;
            }

            var pending = new PendingPhotoUpload
            {
                RecordId = record.Id,
                EmployeeId = employee.Id,
                Bytes = bytes,
                NextAttemptUtc = DateTime.UtcNow,
            };
            _db.PendingPhotoUploads.Add(pending);
            await _db.SaveChangesAsync();
            _photoQueue.MarkEnqueued();
            _photoQueue.PendingDelta(+1);

            // The worker has no request to resolve a tenant from, so hand it the one this record was
            // just written under. Losing the hint costs one poll interval, nothing more.
            _photoQueue.HintReady(new PhotoUploadHint(_db.CurrentTenantId, pending.Id));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Photo audit: failed to queue check-in photo for employee {EmployeeId}, record {RecordId}", employee.Id, record.Id);
            return false;
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
        AttendanceRecord record, Employee employee, Location location, EffectiveShift shift, DateTime nowUtc, string? ip,
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
            earlyDeparture = IsEarlyDeparture(shift.End, shift.LateThresholdMinutes, nowUtc, _timeZone)
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

    /// <summary>
    /// The hours that apply to this employee: their assigned shift ("növbə") if they are on one, else
    /// their own WorkStart/WorkEnd, else the location's. The rule itself lives in
    /// <see cref="EffectiveShift.Resolve(Employee, Schedule?, Location)"/> — the scan must judge a day
    /// exactly the way the reports later will, so neither side is allowed its own copy of it.
    ///
    /// One extra read, and only for an employee actually on a shift.
    /// </summary>
    internal async Task<EffectiveShift> ResolveShiftAsync(Employee employee, Location location)
    {
        var schedule = employee.ScheduleId is Guid id
            ? await _db.Schedules.FirstOrDefaultAsync(s => s.Id == id, HttpContext.RequestAborted)
            : null;
        return EffectiveShift.Resolve(employee, schedule, location);
    }

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
