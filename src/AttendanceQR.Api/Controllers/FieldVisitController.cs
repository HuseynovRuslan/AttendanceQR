using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
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
    private readonly IDailySummaryService _summaries;
    private readonly ILogger<FieldVisitController> _logger;

    public FieldVisitController(
        AppDbContext db, IPhotoStorageService photoStorage, IPushNotifier notifier, AppOptions options,
        IDailySummaryService summaries, ILogger<FieldVisitController> logger)
    {
        _db = db;
        _photoStorage = photoStorage;
        _notifier = notifier;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _summaries = summaries;
        _logger = logger;
    }

    /// <summary>Caps on a manager-typed checklist. Applied by trimming, never by rejecting.</summary>
    private const int MaxChecklistItems = 10;
    private const int MaxChecklistLabel = 120;

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

        // Today's visits — AND yesterday's, if one is still open.
        //
        // This list decided what the worker's screen showed, and «today» stopped being true at
        // midnight for anyone whose shift crosses it. A café worker on a 16:00–00:00 shift checked in
        // at 15:29, worked, and pressed «Sahədən çıxdım» at 00:36 — by then her open visit belonged to
        // «yesterday» and had vanished from her screen, so the app had nothing to close. She pressed
        // «Sahədəyəm» again and checked straight out: a twelve-second visit dated today, while the
        // real nine-hour visit stayed open forever and her day was recorded as QAYIB. Twice.
        //
        // Two guards, and both are needed:
        //   • before noon — the same pivot the overnight attendance calculation uses. A shift that
        //     crossed midnight is finished by then; anything still open at noon is a forgotten
        //     check-out and belongs to the admin's force-checkout, not to a worker pressing a button
        //     a day late.
        //   • within CarryOverHours of the check-in — so a day worker who forgot to check out
        //     yesterday morning is NOT shown a live visit today, where closing it would record
        //     twenty-five hours. The window is generous enough for a long night and far too short
        //     for a forgotten day.
        var yesterday = today.AddDays(-1);
        var nowUtc = DateTime.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _timeZone);
        var carryOver = nowLocal.Hour < 12;

        var visits = await _db.FieldVisits
            .Where(v => v.EmployeeId == me
                        && v.Status != FieldVisitStatus.Cancelled
                        && (v.VisitDate == today
                            || (carryOver
                                && v.VisitDate == yesterday
                                && v.Status == FieldVisitStatus.CheckedIn
                                && v.CheckInAtUtc != null
                                && v.CheckInAtUtc > nowUtc.AddHours(-CarryOverHours))))
            .OrderBy(v => v.Status).ThenByDescending(v => v.CreatedAtUtc)
            .ToListAsync(ct);

        var assignerIds = visits.Where(v => v.AssignedByEmployeeId != null).Select(v => v.AssignedByEmployeeId!.Value).Distinct().ToList();
        var assigners = await _db.Employees.Where(e => assignerIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var checklists = await ChecklistByVisitAsync(visits.Select(v => v.Id).ToList(), ct);

        return Ok(visits.Select(v => Project(v, assigners, checklists.GetValueOrDefault(v.Id))));
    }

    /// <summary>
    /// How long after its check-in a still-open visit can be carried into the next morning.
    ///
    /// Long enough for the longest real night (a 16:00 start finishing at 02:00 is ten hours) and far
    /// short of a forgotten day-shift visit seen the following morning, which would be twenty-five.
    /// </summary>
    private const int CarryOverHours = 16;

    /// <summary>
    /// GET /api/field-visits/sites — the company's branches, nearest first, for the worker to pick from.
    ///
    /// The self-report sheet asked «Ünvan / obyekt» with an autofocused text box, so the keyboard
    /// opened and people typed whatever came to mind. Production shows what that produces:
    /// «Obyektdeyem», «Obyektdəyəm», «Obyekt deyem», «Obyektdeyem» — four spellings of a sentence
    /// that is not a place at all — plus «Nərimanov ofisi» / «Nerimanov» and «Konqresin parkındakı
    /// kafe» / «Kongres parkindaki kafe» for one office and one café. A board built on that cannot
    /// tell two parks apart, which is exactly what a manager reported.
    ///
    /// A list fixes it at the source, and sorting by distance makes the right answer the first one:
    /// the worker is STANDING at the place, and the phone already knows where that is.
    ///
    /// Gated on the same flag as self-reporting itself — a branch list is not secret, but there is no
    /// reason for anyone who cannot file a visit to fetch it.
    /// </summary>
    [HttpGet("sites")]
    public async Task<IActionResult> Sites([FromQuery] double? lat, [FromQuery] double? lng)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;

        if (!await _db.Employees.AnyAsync(e => e.Id == me && e.CanFieldCheckIn, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "FieldCheckInNotAllowed" });

        var sites = await _db.Locations
            .Where(l => l.IsActive)
            .Select(l => new { l.Id, l.Name, l.Latitude, l.Longitude })
            .ToListAsync(ct);

        var ordered = lat is double la && lng is double lo
            ? sites
                .Select(x => new { x.Id, x.Name, Distance = (int?)Math.Round(MetresBetween(la, lo, x.Latitude, x.Longitude)) })
                .OrderBy(x => x.Distance)
                .ToList()
            : sites
                .Select(x => new { x.Id, x.Name, Distance = (int?)null })
                .OrderBy(x => x.Name)
                .ToList();

        return Ok(ordered.Select(x => new { id = x.Id, name = x.Name, distanceMeters = x.Distance }));
    }

    /// <summary>Great-circle metres. Same formula as the client's, so a distance shown on the phone
    /// and one stored on the visit cannot disagree.</summary>
    private static double MetresBetween(double aLat, double aLng, double bLat, double bLng)
    {
        const double r = 6371000;
        var p1 = aLat * Math.PI / 180;
        var p2 = bLat * Math.PI / 180;
        var dp = (bLat - aLat) * Math.PI / 180;
        var dl = (bLng - aLng) * Math.PI / 180;
        var h = Math.Sin(dp / 2) * Math.Sin(dp / 2)
              + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    // POST /api/field-visits/start — worker self-reports an ad-hoc visit: created + checked in at once.
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartFieldVisitRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;
        var now = DateTime.UtcNow;

        // Self-report is gated: only an employee an admin has marked as a field worker may create one.
        // Enforced here, not just hidden in the app — the flag is the permission, the UI is a courtesy.
        if (!await _db.Employees.AnyAsync(e => e.Id == me && e.CanFieldCheckIn, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "FieldCheckInNotAllowed" });

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

        // Return the checklist with the arrival, so the list appears the instant the worker is on site
        // rather than after the next /mine refresh.
        var items = await _db.FieldVisitChecklistItems
            .Where(i => i.FieldVisitId == visit.Id).OrderBy(i => i.SortOrder).ToListAsync(ct);
        return Ok(Project(visit, null, items));
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

        // Already left? Answer OK and keep the original departure time. The response to the first
        // call is easily lost — a 502 mid-deploy, an LTE/wifi handover — and the app then retries.
        // Rejecting the retry would tell a worker who HAS clocked out that they failed to, forever,
        // and would strand the work photo that is sent right after this returns. Same idempotency the
        // scan path gets from ProcessedScan, and the same rule the work-photo upload already follows.
        if (visit.Status == FieldVisitStatus.Completed && visit.CheckOutAtUtc is not null)
        {
            var already = await _db.FieldVisitChecklistItems
                .Where(i => i.FieldVisitId == visit.Id).OrderBy(i => i.SortOrder).ToListAsync(ct);
            return Ok(Project(visit, null, already));
        }
        if (visit.Status != FieldVisitStatus.CheckedIn)
            return BadRequest(new { error = "NotCheckedIn" });

        // 1. THE PAY-CRITICAL WRITE, committed alone before anything optional runs. Coordinates may be
        //    null (no GPS in a basement) — that flags the visit, it never refuses the departure.
        visit.Status = FieldVisitStatus.Completed;
        visit.CheckOutAtUtc = DateTime.UtcNow;
        visit.CheckOutLatitude = req.Latitude;
        visit.CheckOutLongitude = req.Longitude;
        await _db.SaveChangesAsync(ct);

        visit.CheckOutPhotoKey = await TryStorePhotoAsync(me, req.PhotoBase64, ct);
        if (visit.CheckOutPhotoKey is not null)
            await _db.SaveChangesAsync(ct);

        // Close the poster day they can no longer come back to. Wrapped, because step 1 has already
        // been committed and nothing below it is allowed to take the departure away again.
        try { await TryCloseOpenAttendanceAsync(visit, ct); }
        catch { /* the field check-out stands; an admin can still close the day by hand */ }

        // 2. Ticks — advisory, and wrapped so they can never undo step 1. The body carries the ABSOLUTE
        //    set of done ids, so this also reconciles ticks whose own POST never reached the server,
        //    and replaying the same check-out changes nothing.
        var items = await _db.FieldVisitChecklistItems
            .Where(i => i.FieldVisitId == visit.Id).OrderBy(i => i.SortOrder).ToListAsync(ct);
        if (req.DoneItemIds is not null && items.Count > 0)
        {
            try
            {
                var done = req.DoneItemIds.ToHashSet();
                foreach (var it in items)
                {
                    var want = done.Contains(it.Id);
                    if (it.IsDone == want)
                        continue; // an identical replay leaves DoneAtUtc alone
                    it.IsDone = want;
                    it.DoneAtUtc = want ? DateTime.UtcNow : null;
                }
                await _db.SaveChangesAsync(ct);
            }
            catch { /* the check-out already stands — a tick is never worth losing it */ }
        }

        return Ok(Project(visit, null, items));
    }

    // POST /api/field-visits/{id}/checklist/{itemId} — the worker ticks or unticks one line.
    // Only the visit's OWN worker: a manager tick would be worthless as evidence. Failing here is never
    // fatal — the app keeps the tick locally and the check-out's DoneItemIds reconciles it.
    [HttpPost("{id:guid}/checklist/{itemId:guid}")]
    public async Task<IActionResult> SetChecklistItem(Guid id, Guid itemId, [FromBody] SetChecklistItemRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;

        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == me, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        // Tickable before arrival as well as during the work — a "check in first" rule would be a block.
        // Frozen once Completed: the check-out payload is the last word, nobody edits evidence after.
        if (visit.Status is not (FieldVisitStatus.Assigned or FieldVisitStatus.CheckedIn))
            return BadRequest(new { error = "VisitClosed" });

        // itemId is re-checked against the visit so an item from another visit cannot be written here.
        var item = await _db.FieldVisitChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.FieldVisitId == id, ct);
        if (item is null)
            return NotFound(new { error = "ItemNotFound" });

        item.IsDone = req.IsDone;                                   // absolute set, never a toggle
        item.DoneAtUtc = req.IsDone ? item.DoneAtUtc ?? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = item.Id, isDone = item.IsDone, doneAtUtc = item.DoneAtUtc });
    }

    // POST /api/field-visits/{id}/work-photo — İŞ ŞƏKLİ, sent AFTER the check-out is recorded so a
    // failed upload can never cost the worker their departure record. ALWAYS 200, and `stored` tells
    // the truth so the app can offer a retry instead of claiming a success that never happened.
    [HttpPost("{id:guid}/work-photo")]
    public async Task<IActionResult> UploadWorkPhoto(Guid id, [FromBody] WorkPhotoRequest req)
    {
        var ct = HttpContext.RequestAborted;
        var me = Me;

        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == me, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (visit.Status is not (FieldVisitStatus.CheckedIn or FieldVisitStatus.Completed))
            return BadRequest(new { error = "NotOnSite" });
        // Idempotent: retrying an upload whose 200 the app never saw is a no-op, not a second object.
        if (visit.WorkPhotoKey is not null)
            return Ok(new { stored = true });

        byte[] bytes;
        try { bytes = DecodeImage(req.PhotoBase64); }
        catch { return Ok(new { stored = false, error = "Decode" }); }
        // A rear-camera frame is not a 900px selfie, so the cap is higher than TryStorePhotoAsync's.
        if (bytes.Length == 0 || bytes.Length > 4 * 1024 * 1024)
            return Ok(new { stored = false, error = "TooLarge" });

        try
        {
            visit.WorkPhotoKey = await _photoStorage.UploadFieldWorkPhotoAsync(_db.CurrentTenantId, visit.Id, bytes, ct);
            visit.WorkPhotoAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new { stored = true });
        }
        catch
        {
            return Ok(new { stored = false, error = "Storage" });
        }
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
        // Defence in depth: the assignable list already filters to field workers, but never trust it.
        if (!worker.CanFieldCheckIn)
            return BadRequest(new { error = "NotFieldWorker" });

        // A manager may only assign to a Role==Employee worker in their own branches — never to an
        // admin or a fellow manager (the management rule, not the looser visibility one).
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), req.EmployeeId, ct))
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

        // The checklist rides in the same save as the visit — a visit whose instructions half-arrived
        // would be worse than one with none. TenantId is stamped for us.
        var lines = CleanChecklist(req.Checklist);
        for (var i = 0; i < lines.Count; i++)
            _db.FieldVisitChecklistItems.Add(new FieldVisitChecklistItem
            {
                FieldVisitId = visit.Id,
                Label = lines[i],
                SortOrder = i,
            });

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
        var me = Me;
        var query = _db.FieldVisits.Where(v => v.VisitDate == day);
        if (managed != null)
            query = query.Where(v => v.EmployeeId == me || _db.Employees.Any(e =>
                e.Id == v.EmployeeId && managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee));

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
            .Select(e => new { e.Id, e.FullName, e.PhoneNumber, e.LocationId })
            .ToDictionaryAsync(e => e.Id, e => e, ct);

        // The worker's own BRANCH, which this board did not carry at all.
        //
        // Its only «where» column is TargetLabel — free text the worker types — and on production that
        // is «Obyektdeyem» for most of them: not a place, the sentence "I am at the site", in four
        // spellings. So a manager running two parks opened the day's visits and every row read the
        // same, with nothing on the screen able to tell Mərkəz from Dədə Qorqud. The branch is a fact
        // the system already holds; it simply was not projected.
        var branchOf = await _db.Locations
            .Select(l => new { l.Id, l.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // One grouped query over the already-scoped visits — never a lookup per row: this board
        // re-polls every 30 seconds, so an N+1 here is 40 extra queries every half minute, all day.
        var visitIds = visits.Select(v => v.Id).ToList();
        var checklistCounts = visitIds.Count == 0
            ? new Dictionary<Guid, (int Total, int Done)>()
            : (await _db.FieldVisitChecklistItems
                .Where(i => visitIds.Contains(i.FieldVisitId))
                .GroupBy(i => i.FieldVisitId)
                .Select(g => new { VisitId = g.Key, Total = g.Count(), Done = g.Count(i => i.IsDone) })
                .ToListAsync(ct))
              .ToDictionary(x => x.VisitId, x => (x.Total, x.Done));

        var rows = visits.Select(v =>
        {
            names.TryGetValue(v.EmployeeId, out var emp);
            checklistCounts.TryGetValue(v.Id, out var checks);
            var assigner = v.AssignedByEmployeeId != null && names.TryGetValue(v.AssignedByEmployeeId.Value, out var a) ? a.FullName : null;
            int? durationMinutes = v.CheckInAtUtc is DateTime ci && v.CheckOutAtUtc is DateTime co
                ? (int)Math.Round((co - ci).TotalMinutes) : null;
            return new
            {
                id = v.Id,
                employeeId = v.EmployeeId,
                employeeName = emp?.FullName ?? "(silinib)",
                phone = emp?.PhoneNumber,
                locationId = emp?.LocationId,
                locationName = emp is null ? "" : branchOf.GetValueOrDefault(emp.LocationId, ""),
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
                // These two keep their original meaning — a SELFIE is present — and stay untouched;
                // no work-photo state is smuggled into them.
                hasCheckInPhoto = v.CheckInPhotoKey != null,
                hasCheckOutPhoto = v.CheckOutPhotoKey != null,
                hasWorkPhoto = v.WorkPhotoKey != null,
                checklistTotal = checks.Total,
                checklistDone = checks.Done,
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
        var query = _db.Employees.Where(e => e.IsActive && e.CanFieldCheckIn);
        if (managed != null)
            query = query.Where(e => managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee);
        var people = await query
            .OrderBy(e => e.FullName)
            .Select(e => new { id = e.Id, fullName = e.FullName })
            .ToListAsync(ct);
        return Ok(people);
    }

    /// <summary>
    /// POST /api/field-visits/{id}/discard — a visit that happened but records no work.
    ///
    /// Cancel beside it only touches an ASSIGNED visit, on the reasoning that a started one is
    /// evidence. That left no way at all to remove a COMPLETED visit that is not evidence of
    /// anything, and the case that proved it was one the app itself created: a café worker pressed
    /// «Sahədən çıxdım» after midnight, her real open visit had rolled into «yesterday» and vanished
    /// from her screen, so the app opened a NEW visit and closed it twelve seconds later. Two of
    /// those sat in her record looking like work.
    ///
    /// Not deleted — set to Cancelled, with who and why on the audit line. A row that vanishes is a
    /// row nobody can ask about, and «why is there a gap on the 2nd» is exactly the question these
    /// get asked.
    ///
    /// Deliberately NOT guarded by "only if it looks bogus". A rule that tried to recognise one would
    /// be wrong about somebody's genuinely short visit, and refusing an admin who has looked at it
    /// and judged it false is how people end up editing the database by hand — which is precisely
    /// what this endpoint exists to stop.
    /// </summary>
    [HttpPost("{id:guid}/discard")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Discard(Guid id, [FromBody] DiscardVisitRequest? request = null)
    {
        var ct = HttpContext.RequestAborted;
        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
        if (visit.Status == FieldVisitStatus.Cancelled)
            return BadRequest(new { error = "AlreadyCancelled" });

        var minutes = visit.CheckInAtUtc is DateTime a && visit.CheckOutAtUtc is DateTime b
            ? (int)Math.Round((b - a).TotalMinutes)
            : (int?)null;

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = visit.EmployeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = $"Field visit discarded by {Me}: {visit.VisitDate:yyyy-MM-dd} "
                   + $"{visit.CheckInAtUtc:HH:mm}–{(visit.CheckOutAtUtc is null ? "—" : visit.CheckOutAtUtc.Value.ToString("HH:mm"))} UTC"
                   + (minutes is int m ? $" ({m} dəq)" : "")
                   + (string.IsNullOrWhiteSpace(request?.Reason) ? "" : $" — {request!.Reason!.Trim()}"),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

        visit.Status = FieldVisitStatus.Cancelled;
        await _db.SaveChangesAsync(ct);

        // The day has to be re-judged without it, or the summary keeps counting a visit that is no
        // longer there. Today is refused by the job by design and needs no rebuild — today is
        // computed live everywhere.
        try { await _summaries.GenerateForDateAsync(visit.VisitDate, ct); }
        catch (Exception ex) { _logger.LogError(ex, "Visit {Id} discarded, summary rebuild failed", visit.Id); }

        return Ok(new { id = visit.Id, status = visit.Status.ToString(), minutes });
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
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
        if (visit.Status != FieldVisitStatus.Assigned)
            return BadRequest(new { error = "AlreadyStarted" });
        visit.Status = FieldVisitStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = visit.Id, status = visit.Status.ToString() });
    }

    // POST /api/field-visits/{id}/force-checkout — admin closes a visit the worker never checked out of.
    // A CheckedIn visit whose day has passed reads as zero field time (no duration), which pays wrong;
    // this is the field-side counterpart of /admin/open-records. The admin doesn't know the real
    // departure time, so the checkout is stamped "now" (no GPS) and flagged — a cleanup, not a record.
    [HttpPost("{id:guid}/force-checkout")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ForceCheckOut(Guid id, [FromBody] ForceCheckOutRequest? request = null)
    {
        var ct = HttpContext.RequestAborted;
        var visit = await _db.FieldVisits.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
        if (visit.Status != FieldVisitStatus.CheckedIn)
            return BadRequest(new { error = "NotCheckedIn" });

        // «Now» is the fallback, not the rule. This endpoint stamped the current time unconditionally,
        // on the reasoning that an admin does not know when the worker left — usually true, and
        // exactly wrong in the case that made this matter: a visit left open on 1 September, closed on
        // the 4th, would have recorded three days of work. The admin often DOES know the time (the
        // worker told them, or a phantom visit records the minute they tried to check out), and when
        // they do they must be able to say it.
        var leftAt = request?.AtUtc ?? DateTime.UtcNow;
        if (visit.CheckInAtUtc is DateTime ci && leftAt <= ci)
            return BadRequest(new { error = "BeforeCheckIn" });
        if (leftAt > DateTime.UtcNow.AddMinutes(5))
            return BadRequest(new { error = "InFuture" });

        visit.Status = FieldVisitStatus.Completed;
        visit.CheckOutAtUtc = leftAt;
        // No CheckOutLatitude/Longitude — the admin isn't on site; distance stays unmeasured.
        await _db.SaveChangesAsync(ct);

        // Same follow-through as the worker's own check-out: this visit is being closed precisely
        // because nobody is going to pass the poster, so the open day would otherwise stay open.
        var closedDay = false;
        try { closedDay = await TryCloseOpenAttendanceAsync(visit, ct); }
        catch { /* the visit is closed; the day can still be closed by hand */ }

        // Rebuild that day's stored summary. Without it the correction is invisible: a PAST day is
        // read from DailySummaries, not computed, so a visit fixed today would still report the
        // Qayıb the broken visit produced. Today is refused by the job by design (today is computed
        // live everywhere), which is why this is safe to call unconditionally.
        try { await _summaries.GenerateForDateAsync(visit.VisitDate, ct); }
        catch (Exception ex) { _logger.LogError(ex, "Field visit {Id} closed, summary rebuild failed", visit.Id); }

        return Ok(new { id = visit.Id, status = visit.Status.ToString(), closedAttendanceDay = closedDay });
    }


    /// <summary>
    /// Closes the employee's own poster check-in for the same day, at the moment they left the site.
    ///
    /// A worker sent to a site checks in at the poster in the morning, spends the day out, and goes
    /// straight home. The poster is never passed again, so their attendance record kept a check-in and
    /// no check-out — and an open record scores ZERO hours, not partial ones. Until now the only fix
    /// was an admin spotting it on /admin/open-records and typing the time in by hand, which means the
    /// worker's pay depended on somebody noticing.
    ///
    /// Rules this deliberately keeps narrow, because it writes to a pay-critical record:
    ///   • only a record that is genuinely OPEN — never touches a completed day;
    ///   • only when the check-in precedes the departure, so it can never produce negative hours;
    ///   • the field visit's own date, so a stale visit cannot close an unrelated day;
    ///   • marked with the visit id, so the origin of the time stays visible to the admin.
    ///
    /// Advisory in the sense that matters: it runs AFTER the field check-out is committed and can
    /// never undo it. It is not advisory in what it produces — this is the worker's day.
    /// </summary>
    private async Task<bool> TryCloseOpenAttendanceAsync(FieldVisit visit, CancellationToken ct)
    {
        if (visit.CheckOutAtUtc is not DateTime leftAt)
            return false;

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(
            r => r.EmployeeId == visit.EmployeeId
                 && r.AttendanceDate == visit.VisitDate
                 && r.CheckInAtUtc != null
                 && r.CheckOutAtUtc == null, ct);

        // No open record is the normal case for a QR-less field day: there was no poster scan at all,
        // and the reporting layer already synthesises the day from the visit itself.
        if (record is null || record.CheckInAtUtc >= leftAt)
            return false;

        record.CheckOutAtUtc = leftAt;
        record.ClosedByFieldVisitId = visit.Id;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // The old GET /{id}/photos is DELETED, not hidden. It minted presigned SELFIE urls (CheckInPhotoKey
    // / CheckOutPhotoKey) for the field board, and its only consumer — the admin overlay — no longer
    // asks for them. Every worker path sends photoBase64: null, so it returned {null,null} in
    // production: dead code whose only future was to become a face-browsing surface the moment anyone
    // wired a camera to it. Same discipline as the Foto Audit removal — delete the route, don't hide
    // the link. The two selfie columns stay on the entity, unwritten, so a future REASON-GATED field
    // selfie has its slot and has to be argued for on its own merits.

    // GET /api/field-visits/{id}/checklist — the lines + per-item tick times, for the visit detail.
    [HttpGet("{id:guid}/checklist")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Checklist(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var visit = await _db.FieldVisits.Where(v => v.Id == id)
            .Select(v => new { v.EmployeeId }).FirstOrDefaultAsync(ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var items = await _db.FieldVisitChecklistItems
            .Where(i => i.FieldVisitId == id)
            .OrderBy(i => i.SortOrder)
            .Select(i => new { id = i.Id, label = i.Label, sortOrder = i.SortOrder, isDone = i.IsDone, doneAtUtc = i.DoneAtUtc })
            .ToListAsync(ct);
        return Ok(new { items });
    }

    // GET /api/field-visits/{id}/work-photo — a short-lived url for the İŞ ŞƏKLİ. This endpoint can
    // only ever resolve WorkPhotoKey; there is no code path here that returns a selfie. The scope is
    // the same as the deleted selfie one — a work photo still places a named worker somewhere.
    [HttpGet("{id:guid}/work-photo")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> WorkPhoto(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var visit = await _db.FieldVisits
            .Where(v => v.Id == id)
            .Select(v => new { v.EmployeeId, v.WorkPhotoKey, v.WorkPhotoAtUtc })
            .FirstOrDefaultAsync(ct);
        if (visit is null)
            return NotFound(new { error = "VisitNotFound" });
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, Me, User.Role(), visit.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var url = visit.WorkPhotoKey is null ? null : await _photoStorage.GetPresignedUrlAsync(visit.WorkPhotoKey, ct);
        return Ok(new { url, takenAtUtc = visit.WorkPhotoAtUtc });
    }

    // ---- helpers ----

    private object Project(FieldVisit v, Dictionary<Guid, string>? assigners, IReadOnlyList<FieldVisitChecklistItem>? items = null)
    {
        string? assignedBy = v.AssignedByEmployeeId != null && assigners != null && assigners.TryGetValue(v.AssignedByEmployeeId.Value, out var n) ? n : null;
        var list = items ?? Array.Empty<FieldVisitChecklistItem>();
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
            // Always an array, never null — the app renders nothing when it is empty.
            checklist = list.OrderBy(i => i.SortOrder).Select(i => new
            {
                id = i.Id, label = i.Label, sortOrder = i.SortOrder, isDone = i.IsDone, doneAtUtc = i.DoneAtUtc,
            }),
            checklistTotal = list.Count,
            checklistDone = list.Count(i => i.IsDone),
            hasWorkPhoto = v.WorkPhotoKey != null,
        };
    }

    /// <summary>The checklist lines of the given visits, grouped by visit. One query, never per-row.</summary>
    private async Task<Dictionary<Guid, List<FieldVisitChecklistItem>>> ChecklistByVisitAsync(
        List<Guid> visitIds, CancellationToken ct)
    {
        if (visitIds.Count == 0)
            return new Dictionary<Guid, List<FieldVisitChecklistItem>>();
        var items = await _db.FieldVisitChecklistItems
            .Where(i => visitIds.Contains(i.FieldVisitId))
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);
        return items.GroupBy(i => i.FieldVisitId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Cleans a manager's typed checklist: trims, drops blanks, de-dupes case-insensitively (a repeated
    /// line is a typo, not a second task), truncates each line and caps the count. Never rejects —
    /// a validation error here would block an assign over a cosmetic problem.
    /// </summary>
    private static List<string> CleanChecklist(IReadOnlyList<string>? raw) =>
        (raw ?? Array.Empty<string>())
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Select(s => s.Length > MaxChecklistLabel ? s[..MaxChecklistLabel] : s)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxChecklistItems)
            .ToList();

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
