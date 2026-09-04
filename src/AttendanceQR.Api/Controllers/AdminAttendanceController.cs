using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin corrections to raw AttendanceRecords — for the "forgot to check out" case a record can
/// otherwise get permanently stuck as Incomplete. Every change is audited (RecordEditedByAdmin)
/// and immediately recomputes that date's DailySummary so reports agree right away.
/// </summary>
[ApiController]
// Manager too, for the two actions a branch manager is actually asked about: seeing which of THEIR
// staff has an unclosed day, and closing it. A day left open reads as zero hours, so leaving it to an
// admin means a manager watching their own people's time silently disappear. Every read below is
// filtered to their branches and every write re-checks the employee — the wider actions (creating a
// record from nothing, re-running face checks tenant-wide) keep their own Admin-only attributes.
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/attendance")]
public class AdminAttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _dailySummaryService;
    private readonly IFaceMatchQueue _faceQueue;
    private readonly IPhotoStorageService _photoStorage;
    private readonly ILogger<AdminAttendanceController> _logger;
    private readonly TimeZoneInfo _timeZone;

    public AdminAttendanceController(
        AppDbContext db, IDailySummaryService dailySummaryService, IFaceMatchQueue faceQueue,
        IPhotoStorageService photoStorage, ILogger<AdminAttendanceController> logger, AppOptions appOptions)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
        _faceQueue = faceQueue;
        _photoStorage = photoStorage;
        _logger = logger;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(appOptions.TimeZone);
    }

    // GET /api/admin/attendance/open — records with a check-in but no check-out, from BEFORE today.
    // These are the "forgot / couldn't scan to check out" days: the nightly summary marks them
    // Incomplete with 0 minutes worked, so a full day silently reads as zero until an admin closes it.
    // Today is excluded on purpose — an open record for today is just someone still at work.
    [HttpGet("open")]
    public async Task<IActionResult> Open()
    {
        // AttendanceDate is stamped from the UTC day at check-in (see AttendanceController.Scan), so
        // the "not today" cutoff uses the same UTC day — no timezone conversion to get out of step.
        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

        // A manager sees their own branches; an admin sees the company. Without this the list was
        // tenant-wide for whoever could reach it, and the 500-row cap would have quietly hidden a
        // manager's own people behind another branch's backlog.
        var role = User.Role();
        var managed = role == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), HttpContext.RequestAborted)
            : null;

        var rows = await (
            from r in _db.AttendanceRecords
            where r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < todayUtc
                  && (managed == null || managed.Contains(r.LocationId))
            join e in _db.Employees on r.EmployeeId equals e.Id
            join l in _db.Locations on r.LocationId equals l.Id
            orderby r.AttendanceDate descending, e.FullName
            select new
            {
                recordId = r.Id,
                employeeId = e.Id,
                employeeName = e.FullName,
                locationName = l.Name,
                attendanceDate = r.AttendanceDate,
                checkInAtUtc = r.CheckInAtUtc
            })
            .Take(500)
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(rows);
    }

    [HttpPut("{recordId:guid}")]
    public async Task<IActionResult> Update(Guid recordId, [FromBody] AdminAttendanceUpdateRequest request)
    {
        if (request.CheckInAtUtc is null && request.CheckOutAtUtc is null)
            return BadRequest(new { error = "NothingToUpdate" });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // A manager may only correct their OWN staff's record. Same boundary as every other manager
        // write: their branches, and Role==Employee — the 2026-08-08 rule that stopped a manager
        // reaching a same-branch admin's account. An admin passes straight through.
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == record.LocationId);
        if (location is null)
            return BadRequest(new { error = "LocationNotFound" });

        var newCheckIn = request.CheckInAtUtc ?? record.CheckInAtUtc;
        var newCheckOut = request.CheckOutAtUtc ?? record.CheckOutAtUtc;

        if (!TryValidateTimes(newCheckIn, newCheckOut, out var error))
            return BadRequest(new { error });

        if (request.CheckInAtUtc is not null)
        {
            record.CheckInAtUtc = request.CheckInAtUtc;
            // Recomputed the same way the scan would have, so an admin correcting a time cannot
            // produce a status the employee's own shift would never have given.
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == record.EmployeeId);
            var shift = employee is null
                ? EffectiveShift.Resolve(null, null, null, 1, null, null, location)
                : EffectiveShift.Resolve(employee, await ScheduleForAsync(employee), location);
            record.Status = AttendanceController.DetermineStatus(
                shift.HoursOn(record.AttendanceDate).Start, shift.LateThresholdMinutes,
                request.CheckInAtUtc.Value, _timeZone);
        }
        if (request.CheckOutAtUtc is not null)
            record.CheckOutAtUtc = request.CheckOutAtUtc;

        var requesterId = User.EmployeeId();
        record.ManualByEmployeeId = requesterId; // this record was touched by hand — attribute it
        await _db.SaveChangesAsync();

        await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, HttpContext.RequestAborted);

        return Ok(Project(record));
    }

    // Open to a manager, scoped exactly as the edit beside it is.
    //
    // It was Admin-only on the reasoning that writing a day out of nothing is a different power from
    // correcting one that exists. True, but it left the manager stuck in the case that actually
    // happens: somebody leaves their phone at home and never scans at all, so there is NO record to
    // correct — and the person who knows they were at work is the manager who saw them. Refusing here
    // did not prevent a wrong day being recorded; it only meant the right one could not be.
    //
    // The two limits that matter are unchanged and enforced below: their own branch, and plain staff
    // only. A manager cannot write a day onto an admin or onto another manager, which is the 2026-08-08
    // boundary — and writing attendance for the account that can reset PINs is not a smaller version
    // of resetting one.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminAttendanceCreateRequest request)
    {
        if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { error = "DateInFuture" });

        if (!TryValidateTimes(request.CheckInAtUtc, request.CheckOutAtUtc, out var error))
            return BadRequest(new { error });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId);
        if (employee is null)
            return BadRequest(new { error = "EmployeeNotFound" });

        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), request.EmployeeId, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == employee.LocationId);
        if (location is null)
            return BadRequest(new { error = "LocationNotFound" });

        if (await _db.AttendanceRecords.AnyAsync(r => r.EmployeeId == request.EmployeeId && r.AttendanceDate == request.Date))
            return Conflict(new { error = "RecordAlreadyExists" });

        var requesterId = User.EmployeeId();
        var record = new AttendanceRecord
        {
            EmployeeId = request.EmployeeId,
            LocationId = employee.LocationId,
            AttendanceDate = request.Date,
            CheckInAtUtc = request.CheckInAtUtc,
            CheckOutAtUtc = request.CheckOutAtUtc,
            ManualByEmployeeId = requesterId, // created by hand, not a scan
            Status = AttendanceController.DetermineStatus(
                EffectiveShift.Resolve(employee, await ScheduleForAsync(employee), location)
                    .HoursOn(request.Date).Start,
                location.LateThresholdMinutes, request.CheckInAtUtc, _timeZone)
        };
        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync();

        await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

        await _dailySummaryService.GenerateForDateAsync(request.Date, HttpContext.RequestAborted);

        return Ok(Project(record));
    }

    // Undo an accidental check-out — clears CheckOutAtUtc so the employee is "checked in, not out"
    // again and can check out properly later. The Update endpoint can't do this (a null CheckOutAtUtc
    // there means "leave as-is"), so an accidental double-scan check-out needs this explicit action.
    [HttpPost("{recordId:guid}/clear-checkout")]
    public async Task<IActionResult> ClearCheckOut(Guid recordId)
    {
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // A manager may only correct their OWN staff's record. Same boundary as every other manager
        // write: their branches, and Role==Employee — the 2026-08-08 rule that stopped a manager
        // reaching a same-branch admin's account. An admin passes straight through.
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        if (record.CheckOutAtUtc is not null)
        {
            record.CheckOutAtUtc = null;
            var requesterId = User.EmployeeId();
            record.ManualByEmployeeId = requesterId; // undoing a check-out is a manual touch
            await _db.SaveChangesAsync();

            await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, HttpContext.Connection.RemoteIpAddress?.ToString());
            await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, HttpContext.RequestAborted);
        }

        return Ok(Project(record));
    }

    /// <summary>
    /// Say something without taking anything away.
    ///
    /// The lighter of the two actions on a suspect selfie, and the one that should usually come
    /// first. The day stands, the pay stands — the employee simply learns that the photograph was
    /// looked at and did not pass. Most «Mismatch» readings are a cap, a low sun or a dark room, and
    /// docking a day's wage over a bad photograph is a far worse mistake than sending a message that
    /// turns out to have been unnecessary.
    ///
    /// It lands in the employee's in-app inbox as well as on their phone: a push banner is gone the
    /// moment it is swiped, and a warning nobody can re-read afterwards is not a warning. The
    /// (EmployeeId, Type, RelatedDate) unique index means a second press cannot accuse the same
    /// person twice for the same day — the guard is the database's, not the button's.
    /// </summary>
    [HttpPost("{recordId:guid}/send-photo-warning")]
    public async Task<IActionResult> SendPhotoWarning(
        Guid recordId,
        [FromServices] IPushNotifier pushNotifier)
    {
        var ct = HttpContext.RequestAborted;

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Same boundary as every other write here. Sending an accusation is not a smaller power than
        // editing a day: it reaches the person directly.
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var already = await _db.EmployeeNotifications.AnyAsync(
            n => n.EmployeeId == record.EmployeeId
                 && n.Type == EmployeeNotificationType.PhotoWarning
                 && n.RelatedDate == record.AttendanceDate, ct);
        if (already)
            return Conflict(new { error = "AlreadyWarned" });

        const string title = "Giriş şəkli yoxlanıldı";
        var body = $"{record.AttendanceDate:dd.MM.yyyy} tarixli girişinizdə çəkilən şəkil üz "
                 + "yoxlamasından keçmədi. Bu dəfə girişiniz qüvvədədir. Zəhmət olmasa növbəti "
                 + "skanda üzünüz aydın görünsün — papaq və eynəyi çıxarın, işıqlı yerdə çəkin.";

        _db.EmployeeNotifications.Add(new EmployeeNotification
        {
            EmployeeId = record.EmployeeId,
            Type = EmployeeNotificationType.PhotoWarning,
            RelatedDate = record.AttendanceDate,
            Title = title,
            Body = body,
            CreatedAtUtc = DateTime.UtcNow,
        });

        // Audited like the void beside it. A message sent to a named person about their honesty is
        // not a smaller act than editing their day, and «kim göndərdi» is the first question asked
        // when it is disputed.
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = record.EmployeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = $"Photo warning sent by {User.EmployeeId()}: {record.AttendanceDate:yyyy-MM-dd}, "
                   + $"face {record.FaceMatchStatus}"
                   + (record.FaceMatchScore is int sc ? $" {sc}%" : ""),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two admins pressed at once; the unique index caught the second. Not an error — the
            // employee has the warning, which is what was asked for.
            return Conflict(new { error = "AlreadyWarned" });
        }

        // Best-effort, and last: the inbox row is the record of what was said, so a phone with no
        // live subscription must not turn this into a failure.
        var reached = 0;
        try
        {
            reached = await pushNotifier.NotifyEmployeesAsync(
                new[] { record.EmployeeId }, title, body, "/notifications", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo warning {RecordId}: push failed", recordId);
        }

        return Ok(new { sent = true, notified = reached });
    }

    /// <summary>
    /// «Saxta giriş» — the selfie is not this person, so the scan does not count.
    ///
    /// Someone photographed a face off a monitor and the face audit scored it 1%. The admin opens the
    /// two photographs side by side, sees it, and needs one action that makes the day honest again.
    ///
    /// VOIDS, IT DOES NOT DELETE — the difference matters more here than anywhere else in this
    /// controller. Delete beside it removes the row AND the selfie; here that selfie is the ENTIRE
    /// evidence for a disciplinary action against a named person, and destroying it in the act of
    /// taking the action leaves nothing to stand behind if they dispute it. The row stays, the
    /// photograph stays, and the day reads Qayıb because every computation skips a voided record.
    ///
    /// Reversible for the same reason: an admin can be wrong about a face, and un-voiding restores
    /// the day exactly. Nothing could do that after a delete.
    ///
    /// The warning to the employee is sent LAST and its failure is not fatal: the record of what
    /// happened must not depend on whether a phone had a live push subscription.
    /// </summary>
    [HttpPost("{recordId:guid}/void-fraud")]
    public async Task<IActionResult> VoidFraud(
        Guid recordId,
        [FromBody] VoidFraudRequest request,
        [FromServices] IPushNotifier pushNotifier)
    {
        var ct = HttpContext.RequestAborted;

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Same boundary as every other write here: a manager reaches only their own branches' plain
        // employees. This one docks a day's pay and sends an accusation, so the check runs first.
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        if (record.VoidedAtUtc is not null)
            return Conflict(new { error = "AlreadyVoided" });

        var requesterId = User.EmployeeId();
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == record.EmployeeId, ct);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        record.VoidedAtUtc = DateTime.UtcNow;
        record.VoidedByEmployeeId = requesterId;
        record.VoidReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Giriş şəkli saxtadır (üz yoxlaması uyğunsuzluğu)"
            : request.Reason.Trim();

        // Spelled out, because the row itself will read as an ordinary voided record a month from now
        // and this line is what says who decided, when, on what evidence, and with what score.
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = record.EmployeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = $"Voided as fraud by {requesterId}: {record.AttendanceDate:yyyy-MM-dd} "
                   + $"{record.CheckInAtUtc:HH:mm} UTC, face {record.FaceMatchStatus}"
                   + (record.FaceMatchScore is int sc ? $" {sc}%" : "")
                   + (record.CheckInPhotoKey is null ? "" : $", photo {record.CheckInPhotoKey}")
                   + $" — {record.VoidReason}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

        // Killing the device is a SEPARATE decision from voiding the day, and the caller makes it.
        // On a shared brigade phone it would lock out everyone who rides on that handset, so it is
        // never implied by the void itself.
        var revoked = 0;
        if (request.RevokeDevice)
        {
            var devices = await _db.DeviceBindings
                .Where(d => d.EmployeeId == employee.Id && d.IsActive)
                .ToListAsync(ct);
            foreach (var d in devices)
            {
                d.IsActive = false;
                d.RevokedAtUtc = DateTime.UtcNow;
            }
            revoked = devices.Count;
            if (revoked > 0)
            {
                _db.AuditLogs.Add(new AuditLog
                {
                    EmployeeId = employee.Id,
                    EventType = AuditEventType.DeviceBindingRevoked,
                    Reason = $"Photo fraud, {revoked} device(s), by {requesterId}",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        // The day has to be re-judged without the voided scan, or the summary keeps its hours and the
        // board disagrees with the records it is built from. Today is deliberately refused by the
        // summary job (today is computed live everywhere), so this is a no-op for a same-day void —
        // which is correct: the live path already skips voided records.
        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, ct);

        // Last, and best-effort. A phone with no live subscription must not make the void fail.
        var reached = 0;
        if (request.NotifyEmployee)
        {
            try
            {
                reached = await pushNotifier.NotifyEmployeesAsync(
                    new[] { employee.Id },
                    "⚠️ Giriş ləğv edildi",
                    $"{record.AttendanceDate:dd.MM.yyyy} tarixli girişiniz ləğv edildi: giriş şəkli "
                    + "yoxlamadan keçmədi. Sualınız varsa rəhbərinizlə danışın.",
                    "/menu", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fraud void {RecordId}: warning push failed", recordId);
            }
        }

        return Ok(new
        {
            voided = recordId,
            employeeId = employee.Id,
            date = record.AttendanceDate,
            devicesRevoked = revoked,
            notified = reached,
        });
    }

    /// <summary>Undo of the above. It exists because the judgement being undone is a judgement about
    /// a photograph of a face, and people get those wrong.</summary>
    [HttpPost("{recordId:guid}/unvoid")]
    public async Task<IActionResult> Unvoid(Guid recordId)
    {
        var ct = HttpContext.RequestAborted;

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
        if (record.VoidedAtUtc is null)
            return Conflict(new { error = "NotVoided" });

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = record.EmployeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = $"Void reversed by {User.EmployeeId()}: {record.AttendanceDate:yyyy-MM-dd}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

        record.VoidedAtUtc = null;
        record.VoidedByEmployeeId = null;
        record.VoidReason = null;
        await _db.SaveChangesAsync(ct);
        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, ct);

        return Ok(new { unvoided = recordId, date = record.AttendanceDate });
    }

    /// <summary>
    /// Removes a record entirely. Not "zero it out" — remove it.
    ///
    /// Needed for a shape the edit beside it cannot fix: a PHANTOM day. Somebody on a night shift that
    /// the system did not know was a night shift scans twice in the morning; the first scan opens a
    /// day that never existed and the second closes it, leaving a seven-minute record on a date the
    /// person was asleep. That row is not merely wrong — it BLOCKS them, because a day with both a
    /// check-in and a check-out makes the next scan `AlreadyCompleted`. Editing cannot help: any
    /// times at all on that date keep the block. The row has to go.
    ///
    /// Deliberately not guarded by "only if it looks like a phantom". A rule that tried to recognise
    /// one would be wrong about somebody's real short shift, and refusing to delete a record an admin
    /// has looked at and judged false is how people end up editing the database by hand instead —
    /// which is exactly what this endpoint exists to stop.
    ///
    /// What survives: the AuditLog row written below, carrying the times being removed, and the scan's
    /// own audit trail. The check-in selfie does NOT survive — a face photo whose record no longer
    /// exists is an orphan nobody can account for, and deleting an employee already purges theirs.
    /// Storage is cleaned after the row is committed and a failure there is logged, not returned: the
    /// record is gone either way, and the audit line carries the key so a leftover can still be found.
    /// </summary>
    [HttpDelete("{recordId:guid}")]
    public async Task<IActionResult> Delete(Guid recordId)
    {
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Same boundary as the edit and the clear-checkout beside it: a manager reaches only their own
        // branches' plain employees. Deletion is not a wider power than editing a day to zero, and the
        // manager is who notices a phantom on their own board — but it is irreversible, so the scope
        // check runs before anything is touched.
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var requesterId = User.EmployeeId();
        var employeeId = record.EmployeeId;
        var date = record.AttendanceDate;

        // Written BEFORE the delete and with the times spelled out: once the row is gone this audit
        // line is the only place that says what was removed, so "record {id}" alone would be useless
        // to whoever later asks where a day went.
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = $"Deleted by {requesterId}: {date:yyyy-MM-dd} "
                   + $"{record.CheckInAtUtc:HH:mm}–{(record.CheckOutAtUtc is null ? "—" : record.CheckOutAtUtc.Value.ToString("HH:mm"))} UTC"
                   + (record.CheckInPhotoKey is null ? "" : $", photo {record.CheckInPhotoKey}"),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

        var photoKey = record.CheckInPhotoKey;
        _db.AttendanceRecords.Remove(record);
        await _db.SaveChangesAsync();

        if (photoKey is not null)
        {
            try
            {
                await _photoStorage.DeleteObjectsAsync(new[] { photoKey });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Record {RecordId} deleted, but its selfie {Key} could not be removed from storage",
                    recordId, photoKey);
            }
        }

        // The day has to be re-judged without it — otherwise the summary keeps the deleted row's
        // hours and the board disagrees with the record it is built from.
        await _dailySummaryService.GenerateForDateAsync(date, HttpContext.RequestAborted);

        return Ok(new { deleted = recordId, employeeId, date });
    }

    /// <summary>
    /// Rebuilds the stored daily summaries for a date range from the underlying records.
    ///
    /// Needed whenever a CONFIGURATION fix changes what a past day means: a night shift assigned to
    /// someone who was on a day shift, a work-days mask corrected, a phantom record deleted — or the
    /// onboarding rule that stopped an import being billed as absenteeism. The records were always
    /// right; the rows derived from them were computed under the wrong assumptions, and until they
    /// are rebuilt every report keeps repeating the old answer.
    ///
    /// Idempotent and additive-safe: it upserts each day and removes rows that should no longer
    /// exist. It reads AttendanceRecords and never writes them, so nothing anybody scanned can be
    /// lost here — the worst case is that it recomputes to the same numbers.
    ///
    /// TODAY is refused. Today is live everywhere else (the board and the reports compute it rather
    /// than read it), and freezing a half-finished day into the table is how a morning's snapshot
    /// used to get shown for the rest of the day.
    ///
    /// Admin-only and capped at 92 days: it walks the whole tenant per day.
    /// </summary>
    [HttpPost("recompute")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Recompute([FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (to < from) (from, to) = (to, from);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        if (to >= today) to = today.AddDays(-1);
        if (to < from)
            return BadRequest(new { error = "NothingToRecompute" });

        if (to.DayNumber - from.DayNumber > 92)
            return BadRequest(new { error = "RangeTooLong", maxDays = 92 });

        var days = 0;
        var employees = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            employees += await _dailySummaryService.GenerateForDateAsync(d, HttpContext.RequestAborted);
            days++;
        }

        _logger.LogInformation("Recomputed {Days} days ({From}..{To}) by {Requester}",
            days, from, to, User.EmployeeId());

        return Ok(new { days, employees, from, to });
    }

    // Re-queue a background face-match for every record that has a check-in photo — e.g. after the
    // references were corrected, to (re)score the history. Returns how many were queued.
    // Admin-only by its own attribute: a paid Rekognition call per record, tenant-wide.
    [HttpPost("recheck-faces")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RecheckFaces()
    {
        var ids = await _db.AttendanceRecords
            .Where(r => r.CheckInPhotoKey != null)
            .Select(r => r.Id)
            .ToListAsync(HttpContext.RequestAborted);
        foreach (var id in ids)
            _faceQueue.Enqueue(_db.CurrentTenantId, id);
        return Ok(new { queued = ids.Count });
    }

    private static bool TryValidateTimes(DateTime? checkIn, DateTime? checkOut, out string? error)
    {
        error = null;
        var now = DateTime.UtcNow;
        if (checkIn is not null && checkIn.Value > now) { error = "CheckInInFuture"; return false; }
        if (checkOut is not null && checkOut.Value > now) { error = "CheckOutInFuture"; return false; }
        if (checkIn is not null && checkOut is not null && checkOut.Value < checkIn.Value) { error = "CheckOutBeforeCheckIn"; return false; }
        return true;
    }

    private async Task WriteAuditAsync(Guid employeeId, Guid requesterId, Guid recordId, string? ip)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = $"By {requesterId}, record {recordId}",
            IpAddress = ip
        });
        await _db.SaveChangesAsync();
    }

    private static object Project(AttendanceRecord r) => new
    {
        recordId = r.Id,
        employeeId = r.EmployeeId,
        attendanceDate = r.AttendanceDate,
        checkInAtUtc = r.CheckInAtUtc,
        checkOutAtUtc = r.CheckOutAtUtc,
        status = r.Status.ToString()
    };

    /// <summary>The employee's assigned shift, or null when they are not on one.</summary>
    private Task<Schedule?> ScheduleForAsync(Employee employee) =>
        employee.ScheduleId is Guid id
            ? _db.Schedules.FirstOrDefaultAsync(sc => sc.Id == id, HttpContext.RequestAborted)
            : Task.FromResult<Schedule?>(null);
}
