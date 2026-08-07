using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Field / mobile attendance — a worker sent to an ad-hoc site with no printed QR poster. Proof of
/// presence is GPS + selfie + timestamp (the same machinery a scan uses), not a scan. A manager assigns
/// a visit (optionally pinning a target) or a worker self-reports one; arrival and departure are both
/// captured. Nothing here BLOCKS the worker — a missing photo or a far GPS flags the visit for the
/// manager, it never refuses to record it (a field worker's pay depends on the record, exactly like a scan).
/// </summary>
[ApiController]
[Authorize]
[Route("api/field-visits")]
public class FieldVisitController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IPushNotifier _notifier;
    private readonly TimeZoneInfo _timeZone;

    public FieldVisitController(AppDbContext db, IPhotoStorageService photoStorage, IPushNotifier notifier, AppOptions options)
    {
        _db = db;
        _photoStorage = photoStorage;
        _notifier = notifier;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    private Guid Me => User.EmployeeId();
    private DateOnly TodayLocal() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

    // Null for an Admin (the whole tenant); a Manager's ManagedLocations otherwise. Every other
    // Admin+Manager surface enforces this — a manager only ever reaches their own branches' staff
    // (their phone, live GPS and selfies), never another branch's.
    private async Task<List<Guid>?> ManagedScopeAsync(CancellationToken ct)
        => User.Role() == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, Me, ct)
            : null;

    // ---------------------------------------------------------------- worker (any authenticated) ----

    // GET /api/field-visits/mine — the caller's own visits for today (assigned to them or self-started).
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var ct = HttpContext.RequestAborted;
        var today = TodayLocal();
        var me = Me;

        var visits = await _db.FieldVisits
            .Where(v => v.EmployeeId == me && v.VisitDate == today && v.Status != FieldVisitStatus.Cancelled)
            .OrderBy(v => v.Status).ThenByDescending(v => v.CreatedAtUtc)
            .ToListAsync(ct);

        var assignerIds = visits.Where(v => v.AssignedByEmployeeId != null).Select(v => v.AssignedByEmployeeId!.Value).Distinct().ToList();
        var assigners = await _db.Employees.Where(e => assignerIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        return Ok(visits.Select(v => Project(v, assigners)));
    }

    // POST /api/field-visits/start — worker self-reports an ad-hoc visit: created + checked in at once.
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartFieldVisitRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;
        var now = DateTime.UtcNow;

        var visit = new FieldVisit
        {
            EmployeeId = me,
            AssignedByEmployeeId = null,
            VisitDate = TodayLocal(),
            Status = FieldVisitStatus.CheckedIn,
            TargetLabel = string.IsNullOrWhiteSpace(req.TargetLabel) ? null : req.TargetLabel.Trim(),
            CheckInAtUtc = now,
            CheckInLatitude = req.Latitude,
            CheckInLongitude = req.Longitude,
        };
        _db.FieldVisits.Add(visit);
        await _db.SaveChangesAsync(ct);

        visit.CheckInPhotoKey = await TryStorePhotoAsync(me, req.PhotoBase64, ct);
        if (visit.CheckInPhotoKey is not null)
            await _db.SaveChangesAsync(ct);

        return Ok(Project(visit, null));
    }

    // POST /api/field-visits/{id}/check-in — worker arrives at an ASSIGNED visit.
    [HttpPost("{id:guid}/check-in")]
    public async Task<IActionResult> CheckIn(Guid id, [FromBody] FieldCheckInRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;

        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == me, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (visit.Status != FieldVisitStatus.Assigned)
            return BadRequest(new { error = "NotAssigned" }); // already checked in / done / cancelled

        visit.Status = FieldVisitStatus.CheckedIn;
        visit.CheckInAtUtc = DateTime.UtcNow;
        visit.CheckInLatitude = req.Latitude;
        visit.CheckInLongitude = req.Longitude;
        // Advisory distance from the pinned target, if there is one. Never blocks.
        if (visit.TargetLatitude is double tLat && visit.TargetLongitude is double tLng)
            visit.CheckInDistanceMeters = GeoCalculator.DistanceMeters(tLat, tLng, req.Latitude, req.Longitude);
        await _db.SaveChangesAsync(ct);

        visit.CheckInPhotoKey = await TryStorePhotoAsync(me, req.PhotoBase64, ct);
        if (visit.CheckInPhotoKey is not null)
            await _db.SaveChangesAsync(ct);

        return Ok(Project(visit, null));
    }

    // POST /api/field-visits/{id}/check-out — worker leaves the site.
    [HttpPost("{id:guid}/check-out")]
    public async Task<IActionResult> CheckOut(Guid id, [FromBody] FieldCheckOutRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;

        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == me, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (visit.Status != FieldVisitStatus.CheckedIn)
            return BadRequest(new { error = "NotCheckedIn" });

        visit.Status = FieldVisitStatus.Completed;
        visit.CheckOutAtUtc = DateTime.UtcNow;
        visit.CheckOutLatitude = req.Latitude;
        visit.CheckOutLongitude = req.Longitude;
        await _db.SaveChangesAsync(ct);

        visit.CheckOutPhotoKey = await TryStorePhotoAsync(me, req.PhotoBase64, ct);
        if (visit.CheckOutPhotoKey is not null)
            await _db.SaveChangesAsync(ct);

        return Ok(Project(visit, null));
    }

    // ------------------------------------------------------------------ manager / admin (assign, board) ----

    // POST /api/field-visits — a manager assigns a visit to a worker (optionally pinning a target).
    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Assign([FromBody] AssignFieldVisitRequest req)
    {
        var ct = HttpContext.RequestAborted;

        var worker = await _db.Employees.FirstOrDefaultAsync(e => e.Id == req.EmployeeId && e.IsActive, ct);
        if (worker is null)
            return BadRequest(new { error = "EmployeeNotFound" });

        // A manager may only assign to a worker in their own branches.
        if (!await LocationScopeRules.CanAccessEmployeeAsync(_db, Me, User.Role(), req.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        // A target is all-or-nothing on the coordinates — a lone latitude can't be measured against.
        var hasLat = req.TargetLatitude is not null;
        var hasLng = req.TargetLongitude is not null;
        if (hasLat != hasLng)
            return BadRequest(new { error = "TargetIncomplete" });

        var visit = new FieldVisit
        {
            EmployeeId = req.EmployeeId,
            AssignedByEmployeeId = Me,
            VisitDate = req.VisitDate ?? TodayLocal(),
            Status = FieldVisitStatus.Assigned,
            AssignedAtUtc = DateTime.UtcNow,
            TargetLabel = string.IsNullOrWhiteSpace(req.TargetLabel) ? null : req.TargetLabel.Trim(),
            TargetLatitude = req.TargetLatitude,
            TargetLongitude = req.TargetLongitude,
            TargetRadiusMeters = hasLat ? (req.TargetRadiusMeters is > 0 ? req.TargetRadiusMeters : 200) : null,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
        };
        _db.FieldVisits.Add(visit);
        await _db.SaveChangesAsync(ct);

        // Best-effort push so the worker sees the task without opening the app — the home banner (from
        // GET /mine) is the reliable surface; this is a nudge on top. Never blocks the assign.
        try
        {
            var where = visit.TargetLabel ?? "Yeni sahə ziyarəti";
            await _notifier.NotifyEmployeesAsync(new[] { visit.EmployeeId }, "Yeni sahə tapşırığı", where, "/field", ct);
        }
        catch { /* push is best-effort */ }

        return Ok(new { id = visit.Id });
    }

    // GET /api/field-visits?date=yyyy-MM-dd — the board for a day (defaults to today).
    [HttpGet]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Board([FromQuery] DateOnly? date)
    {
        var ct = HttpContext.RequestAborted;
        var day = date ?? TodayLocal();

        // A manager sees only their branches' workers' visits (PII: phone, GPS, selfies); an Admin, all.
        var managed = await ManagedScopeAsync(ct);
        var query = _db.FieldVisits.Where(v => v.VisitDate == day);
        if (managed != null)
            query = query.Where(v => _db.Employees.Any(e => e.Id == v.EmployeeId && managed.Contains(e.LocationId)));

        var visits = await query
            .OrderBy(v => v.Status).ThenBy(v => v.CheckInAtUtc)
            .ToListAsync(ct);

        var ids = visits.Select(v => v.EmployeeId)
            .Concat(visits.Where(v => v.AssignedByEmployeeId != null).Select(v => v.AssignedByEmployeeId!.Value))
            .Distinct().ToList();
        // Tenant-scoped: the ids all come from this tenant's visits, so the normal (filtered) query
        // resolves them — no IgnoreQueryFilters (that is reserved for the super-admin surfaces).
        var names = await _db.Employees
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.PhoneNumber })
            .ToDictionaryAsync(e => e.Id, e => e, ct);

        var rows = visits.Select(v =>
        {
            names.TryGetValue(v.EmployeeId, out var emp);
            var assigner = v.AssignedByEmployeeId != null && names.TryGetValue(v.AssignedByEmployeeId.Value, out var a) ? a.FullName : null;
            int? durationMinutes = v.CheckInAtUtc is DateTime ci && v.CheckOutAtUtc is DateTime co
                ? (int)Math.Round((co - ci).TotalMinutes) : null;
            return new
            {
                id = v.Id,
                employeeId = v.EmployeeId,
                employeeName = emp?.FullName ?? "(silinib)",
                phone = emp?.PhoneNumber,
                assignedByName = assigner,
                selfReported = v.AssignedByEmployeeId == null,
                status = v.Status.ToString(),
                targetLabel = v.TargetLabel,
                targetLatitude = v.TargetLatitude,
                targetLongitude = v.TargetLongitude,
                targetRadiusMeters = v.TargetRadiusMeters,
                checkInAtUtc = v.CheckInAtUtc,
                checkInLatitude = v.CheckInLatitude,
                checkInLongitude = v.CheckInLongitude,
                checkInDistanceMeters = v.CheckInDistanceMeters,
                checkOutAtUtc = v.CheckOutAtUtc,
                checkOutLatitude = v.CheckOutLatitude,
                checkOutLongitude = v.CheckOutLongitude,
                durationMinutes,
                hasCheckInPhoto = v.CheckInPhotoKey != null,
                hasCheckOutPhoto = v.CheckOutPhotoKey != null,
                note = v.Note,
            };
        });
        return Ok(rows);
    }

    // GET /api/field-visits/assignable — active workers to assign a visit to (for the assign dropdown).
    [HttpGet("assignable")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Assignable()
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedScopeAsync(ct);
        var query = _db.Employees.Where(e => e.IsActive);
        if (managed != null)
            query = query.Where(e => managed.Contains(e.LocationId));
        var people = await query
            .OrderBy(e => e.FullName)
            .Select(e => new { id = e.Id, fullName = e.FullName })
            .ToListAsync(ct);
        return Ok(people);
    }

    // POST /api/field-visits/{id}/cancel — call off an assignment the worker hasn't started.
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (!await LocationScopeRules.CanAccessEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
        if (visit.Status != FieldVisitStatus.Assigned)
            return BadRequest(new { error = "AlreadyStarted" });
        visit.Status = FieldVisitStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = visit.Id, status = visit.Status.ToString() });
    }

    // GET /api/field-visits/{id}/photos — short-lived presigned selfie URLs for the board.
    [HttpGet("{id:guid}/photos")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Photos(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var visit = await _db.FieldVisits
            .Where(v => v.Id == id)
            .Select(v => new { v.EmployeeId, v.CheckInPhotoKey, v.CheckOutPhotoKey })
            .FirstOrDefaultAsync(ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        // A manager must not pull the selfie of a worker outside their branches.
        if (!await LocationScopeRules.CanAccessEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var checkInUrl = visit.CheckInPhotoKey is null ? null : await _photoStorage.GetPresignedUrlAsync(visit.CheckInPhotoKey, ct);
        var checkOutUrl = visit.CheckOutPhotoKey is null ? null : await _photoStorage.GetPresignedUrlAsync(visit.CheckOutPhotoKey, ct);
        return Ok(new { checkInUrl, checkOutUrl });
    }

    // ---- helpers ----

    private object Project(FieldVisit v, Dictionary<Guid, string>? assigners)
    {
        string? assignedBy = v.AssignedByEmployeeId != null && assigners != null && assigners.TryGetValue(v.AssignedByEmployeeId.Value, out var n) ? n : null;
        return new
        {
            id = v.Id,
            status = v.Status.ToString(),
            selfReported = v.AssignedByEmployeeId == null,
            assignedByName = assignedBy,
            targetLabel = v.TargetLabel,
            targetLatitude = v.TargetLatitude,
            targetLongitude = v.TargetLongitude,
            targetRadiusMeters = v.TargetRadiusMeters,
            checkInAtUtc = v.CheckInAtUtc,
            checkInDistanceMeters = v.CheckInDistanceMeters,
            checkOutAtUtc = v.CheckOutAtUtc,
            note = v.Note,
        };
    }

    // Stores a field selfie, never throwing (a failed/absent photo must not block the visit). A fresh id
    // per photo keeps check-in and check-out from colliding on the same storage key.
    private async Task<string?> TryStorePhotoAsync(Guid employeeId, string? photoBase64, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
            return null;
        try
        {
            var bytes = DecodeImage(photoBase64);
            if (bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024)
                return null;
            return await _photoStorage.UploadCheckInPhotoAsync(employeeId, Guid.NewGuid(), bytes, ct);
        }
        catch
        {
            return null;
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
}
