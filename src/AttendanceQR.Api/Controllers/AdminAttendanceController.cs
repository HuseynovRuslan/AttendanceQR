using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
    [Permission(AccessPermissions.AttendanceView)]
    [HttpGet("open")]
    public async Task<IActionResult> Open()
    {
        // The company day — the same one the scan stamps onto AttendanceDate. It was the UTC day on
        // both sides until 02.10.2026; see AttendanceController.Scan for the night that changed it.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        // A manager sees their own branches; an admin sees the company. Without this the list was
        // tenant-wide for whoever could reach it, and the 500-row cap would have quietly hidden a
        // manager's own people behind another branch's backlog.
        var role = User.Role();
        var managed = role == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), HttpContext.RequestAborted)
            : null;

        var rows = await (
            from r in _db.AttendanceRecords
            where r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < today
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
                checkInAtUtc = r.CheckInAtUtc,
                e.Role,
                EmployeeLocationId = e.LocationId,
            })
            .Take(500)
            .ToListAsync(HttpContext.RequestAborted);

        // The list is what a manager SEES — every day scanned at their branches — but closing one goes
        // through CanManageEmployeeAsync, which stops at plain staff of their own branches. So their own
        // forgotten day and a fellow manager's sat at the top of this list, took a time, and answered
        // «Bağlanmadı» with no reason (Alıyev Nihat, 2026-09-14: five such rows — three Bəbirov Vüsal's,
        // one Əmirov Aydın's, one his own). Each row now says whether THIS caller may close it, by the
        // same rule the write applies, so the screen can say «admin bağlamalıdır» instead of failing.
        var me = User.EmployeeId();
        return Ok(rows.Select(x => new
        {
            x.recordId, x.employeeId, x.employeeName, x.locationName, x.attendanceDate, x.checkInAtUtc,
            // Never one's own day — an admin's included since 2026-10-15 (CanCorrectAttendanceAsync).
            closable = x.employeeId != me
                       && (managed is null
                           // Plain staff, and since 2026-09-18 a fellow manager — both of the caller's own branches.
                           || ((x.Role == EmployeeRole.Employee || x.Role == EmployeeRole.Manager)
                               && managed.Contains(x.EmployeeLocationId))),
        }));
    }

    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpPut("{recordId:guid}")]
    public async Task<IActionResult> Update(Guid recordId, [FromBody] AdminAttendanceUpdateRequest request)
    {
        if (request.CheckInAtUtc is null && request.CheckOutAtUtc is null)
            return BadRequest(new { error = "NothingToUpdate" });
        if (CorrectionReason.Clean(request.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // A manager may only correct their OWN staff's record. Same boundary as every other manager
        // write: their branches, and Role==Employee — the 2026-08-08 rule that stopped a manager
        // reaching a same-branch admin's account. An admin passes straight through — except to their own
        // day, which nobody corrects for themselves (the owner, 2026-10-15) unless nobody else could
        // (CorrectorAsync — the single-admin rule of 2026-10-16).
        var (refusal, self) = await CorrectorAsync(record.EmployeeId, "OutOfScope");
        if (refusal is not null)
            return refusal;
        var before = Times(record);
        var (beforeIn, beforeOut) = (record.CheckInAtUtc, record.CheckOutAtUtc);

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == record.LocationId);
        if (location is null)
            return BadRequest(new { error = "LocationNotFound" });

        var newCheckIn = request.CheckInAtUtc ?? record.CheckInAtUtc;
        var newCheckOut = request.CheckOutAtUtc ?? record.CheckOutAtUtc;

        if (!TryValidateTimes(newCheckIn, newCheckOut, out var error))
            return BadRequest(new { error });

        var requesterId = User.EmployeeId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (self == SelfCorrectionAnswer.AwaitApproval)
        {
            SelfCorrection.Record(_db, record.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.Update,
                beforeIn, beforeOut, newCheckIn, newCheckOut, reason, awaitsApproval: true, ip);
            await _db.SaveChangesAsync();
            return Accepted(new { status = "AwaitingApproval" });
        }

        await ApplyTimesAsync(record, location, request.CheckInAtUtc, request.CheckOutAtUtc, requesterId);
        if (self == SelfCorrectionAnswer.Apply)
            SelfCorrection.Record(_db, record.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.Update,
                beforeIn, beforeOut, record.CheckInAtUtc, record.CheckOutAtUtc, reason, awaitsApproval: false, ip);
        await _db.SaveChangesAsync();

        if (self is null)
            await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, ip, $"{before} → {Times(record)}", reason);

        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, HttpContext.RequestAborted);

        return Ok(Project(record));
    }

    /// <summary>
    /// Closes many forgotten check-outs at once, each at the end of THAT person's own shift on THAT
    /// day — never a blanket hour.
    ///
    /// One at a time, this screen is a chore nobody finishes: a day that is checked in and never out
    /// scores zero hours, the list only grows, and the fix is the same three clicks per row. What made
    /// a batch honest is that the hour is no longer a guess — every employee is on a named shift or
    /// carries their own, so «bitir 18:00» comes from EffectiveShift rather than from a default typed
    /// into a form.
    ///
    /// Deliberately still a BUTTON, not a nightly job. Closing days automatically was offered with the
    /// figures and turned down on 2026-08-11, and for a good reason: an unclosed day is evidence that
    /// somebody did not scan, and a job that quietly tidies it away destroys the evidence and pays the
    /// hours anyway. A person pressing this is vouching for the days on their screen.
    ///
    /// TODAY is never closed, whatever is sent: a shift still running is not a forgotten check-out —
    /// the same rule the board and «Saatlarım» apply.
    /// </summary>
    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpPost("close-open")]
    public async Task<IActionResult> CloseOpenDays([FromBody] CloseOpenDaysRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var ids = (request.RecordIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest(new { error = "NothingSelected" });
        if (ids.Count > MaxBulkClose)
            return BadRequest(new { error = "TooMany" });
        if (CorrectionReason.Clean(request.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var requesterId = User.EmployeeId();
        var todayLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        var records = await _db.AttendanceRecords
            .Where(r => ids.Contains(r.Id) && r.CheckInAtUtc != null && r.CheckOutAtUtc == null)
            .ToListAsync(ct);

        var closed = new List<AttendanceRecord>();
        var ownClosed = new HashSet<Guid>();
        var skipped = 0;
        var awaiting = 0;
        var days = new HashSet<DateOnly>();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        SelfCorrectionVerdict? ownVerdict = null;

        foreach (var record in records)
        {
            if (record.AttendanceDate >= todayLocal) { skipped++; continue; }

            // One's own day: only where nobody else could close it (SelfCorrection) — otherwise skipped, as before.
            SelfCorrectionAnswer? self = null;
            if (record.EmployeeId == requesterId)
            {
                ownVerdict ??= await SelfCorrection.AskAsync(_db, requesterId, User.Role(), User.IsImpersonating(), ct);
                if (ownVerdict.Refused) { skipped++; continue; }
                self = ownVerdict.Answer;
            }
            else if (!await LocationScopeRules.CanCorrectAttendanceAsync(_db, requesterId, User.Role(), record.EmployeeId, ct))
            { skipped++; continue; }

            var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == record.LocationId, ct);
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == record.EmployeeId, ct);
            if (location is null || employee is null) { skipped++; continue; }

            var shift = EffectiveShift.Resolve(employee, await ScheduleForAsync(employee, record.AttendanceDate), location);
            var closeAt = ShiftEndUtc(shift, record.AttendanceDate, record.CheckInAtUtc!.Value);
            if (closeAt <= record.CheckInAtUtc) { skipped++; continue; }

            if (self == SelfCorrectionAnswer.AwaitApproval)
            {
                SelfCorrection.Record(_db, record.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.CloseOpen,
                    record.CheckInAtUtc, null, record.CheckInAtUtc, closeAt, reason, awaitsApproval: true, ip);
                awaiting++;
                continue;
            }

            record.CheckOutAtUtc = closeAt;
            record.ManualByEmployeeId = requesterId; // written by hand, and attributed like any other
            if (self == SelfCorrectionAnswer.Apply)
            {
                SelfCorrection.Record(_db, record.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.CloseOpen,
                    record.CheckInAtUtc, null, record.CheckInAtUtc, closeAt, reason, awaitsApproval: false, ip);
                ownClosed.Add(record.Id);
            }
            closed.Add(record);
            days.Add(record.AttendanceDate);
        }

        if (closed.Count == 0 && awaiting == 0)
            return Ok(new { closed = 0, skipped, awaiting });

        await _db.SaveChangesAsync(ct);

        // One audit row per record, exactly as a single edit writes — a batch must not be cheaper to
        // audit than the twenty clicks it replaces. The batch's one reason goes on every row. One's own day
        // already has its own line (SelfCorrection.Record).
        foreach (var r in closed.Where(r => !ownClosed.Contains(r.Id)))
            await WriteAuditAsync(r.EmployeeId, requesterId, r.Id, ip, $"closed at shift end, {Times(r)}", reason);

        foreach (var day in days)
            await _dailySummaryService.GenerateForDateAsync(day, ct);

        return Ok(new { closed = closed.Count, skipped, awaiting });
    }

    /// <summary>How many days one press may close. High enough for a month's backlog at a big company,
    /// low enough that a malformed call cannot rewrite a year of attendance in one request.</summary>
    private const int MaxBulkClose = 500;

    /// <summary>
    /// The moment that shift ends, in UTC, on that date. An overnight shift (end before start) ends on
    /// the FOLLOWING day — closing a 22:00–06:00 shift at 06:00 of the day it began would put the
    /// check-out sixteen hours before the check-in.
    /// </summary>
    private DateTime ShiftEndUtc(EffectiveShift shift, DateOnly date, DateTime checkInUtc)
    {
        var hours = shift.HoursOn(date);
        var endDate = hours.End < hours.Start ? date.AddDays(1) : date;
        var localEnd = DateTime.SpecifyKind(endDate.ToDateTime(hours.End), DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(localEnd, _timeZone);
        // A day whose scan came in AFTER its own shift end (a late arrival on a short shift) would
        // otherwise close before it opened. Fall back to the check-in itself plus the shift's length.
        if (utc <= checkInUtc)
        {
            var span = hours.End < hours.Start
                ? (TimeOnly.MaxValue - hours.Start) + hours.End.ToTimeSpan()
                : hours.End - hours.Start;
            utc = checkInUtc.Add(span);
        }
        return utc;
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
    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminAttendanceCreateRequest request)
    {
        if (request.Date > DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { error = "DateInFuture" });

        if (!TryValidateTimes(request.CheckInAtUtc, request.CheckOutAtUtc, out var error))
            return BadRequest(new { error });
        if (CorrectionReason.Clean(request.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId);
        if (employee is null)
            return BadRequest(new { error = "EmployeeNotFound" });

        var (refusal, self) = await CorrectorAsync(request.EmployeeId, "Forbidden");
        if (refusal is not null)
            return refusal;

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == employee.LocationId);
        if (location is null)
            return BadRequest(new { error = "LocationNotFound" });

        if (await _db.AttendanceRecords.AnyAsync(r => r.EmployeeId == request.EmployeeId && r.AttendanceDate == request.Date))
            return Conflict(new { error = "RecordAlreadyExists" });

        var requesterId = User.EmployeeId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (self == SelfCorrectionAnswer.AwaitApproval)
        {
            SelfCorrection.Record(_db, request.EmployeeId, null, request.Date, SelfCorrection.Create,
                null, null, request.CheckInAtUtc, request.CheckOutAtUtc, reason, awaitsApproval: true, ip);
            await _db.SaveChangesAsync();
            return Accepted(new { status = "AwaitingApproval" });
        }

        var record = await NewRecordAsync(employee, location, request.Date, request.CheckInAtUtc, request.CheckOutAtUtc, requesterId);
        _db.AttendanceRecords.Add(record);
        if (self == SelfCorrectionAnswer.Apply)
            SelfCorrection.Record(_db, record.EmployeeId, record.Id, request.Date, SelfCorrection.Create,
                null, null, record.CheckInAtUtc, record.CheckOutAtUtc, reason, awaitsApproval: false, ip);
        await _db.SaveChangesAsync();

        if (self is null)
            await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, ip, $"created {request.Date:yyyy-MM-dd} {Times(record)}", reason);

        await _dailySummaryService.GenerateForDateAsync(request.Date, HttpContext.RequestAborted);

        return Ok(Project(record));
    }

    // Undo an accidental check-out — clears CheckOutAtUtc so the employee is "checked in, not out"
    // again and can check out properly later. The Update endpoint can't do this (a null CheckOutAtUtc
    // there means "leave as-is"), so an accidental double-scan check-out needs this explicit action.
    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpPost("{recordId:guid}/clear-checkout")]
    public async Task<IActionResult> ClearCheckOut(
        Guid recordId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttendanceCorrectionRequest? request)
    {
        if (CorrectionReason.Clean(request?.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // A manager may only correct their OWN staff's record. Same boundary as every other manager
        // write: their branches, and Role==Employee — the 2026-08-08 rule that stopped a manager
        // reaching a same-branch admin's account. An admin passes straight through — except to their own day,
        // unless nobody else could (CorrectorAsync).
        var (refusal, self) = await CorrectorAsync(record.EmployeeId, "OutOfScope");
        if (refusal is not null)
            return refusal;

        if (record.CheckOutAtUtc is not null)
        {
            var before = Times(record);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (self == SelfCorrectionAnswer.AwaitApproval)
            {
                SelfCorrection.Record(_db, record.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.ClearCheckout,
                    record.CheckInAtUtc, record.CheckOutAtUtc, record.CheckInAtUtc, null, reason, awaitsApproval: true, ip);
                await _db.SaveChangesAsync();
                return Accepted(new { status = "AwaitingApproval" });
            }
            var beforeOut = record.CheckOutAtUtc;
            record.CheckOutAtUtc = null;
            var requesterId = User.EmployeeId();
            record.ManualByEmployeeId = requesterId; // undoing a check-out is a manual touch
            if (self == SelfCorrectionAnswer.Apply)
                SelfCorrection.Record(_db, record.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.ClearCheckout,
                    record.CheckInAtUtc, beforeOut, record.CheckInAtUtc, null, reason, awaitsApproval: false, ip);
            await _db.SaveChangesAsync();

            if (self is null)
                await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, ip, $"check-out cleared, {before} → {Times(record)}", reason);
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
    [Permission(AccessPermissions.AttendanceFraud)]
    [HttpPost("{recordId:guid}/send-photo-warning")]
    public async Task<IActionResult> SendPhotoWarning(
        Guid recordId,
        [FromBody] PhotoWarningRequest? request,
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

        var already = await _db.EmployeeNotifications
            .Where(n => n.EmployeeId == record.EmployeeId
                        && n.Type == EmployeeNotificationType.PhotoWarning
                        && n.RelatedDate == record.AttendanceDate)
            .Select(n => new { n.CreatedAtUtc, n.AcknowledgedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (already is not null)
            // Not merely "already sent": whether they OPENED it. That is the fact an admin is
            // actually after when they press the button a second time.
            return Conflict(new
            {
                error = "AlreadyWarned",
                sentAtUtc = already.CreatedAtUtc,
                acknowledgedAtUtc = already.AcknowledgedAtUtc,
            });

        // The admin's own words when they wrote any, the neutral template otherwise. Free text
        // because the sentence that fits depends entirely on the two photographs and on the person —
        // «papağı çıxar» and «bu, sən deyilsən» are not the same message, and only the person looking
        // at them knows which one this is. Trimmed and capped; what it SAYS is the sender's to decide,
        // and their name is on the audit line beside it.
        var custom = request?.Message?.Trim();
        var title = string.IsNullOrWhiteSpace(custom) ? "Giriş şəkli yoxlanıldı" : "Rəhbərlikdən mesaj";
        var body = string.IsNullOrWhiteSpace(custom)
            ? $"{record.AttendanceDate:dd.MM.yyyy} tarixli girişinizdə çəkilən şəkil üz "
              + "yoxlamasından keçmədi. Bu dəfə girişiniz qüvvədədir. Zəhmət olmasa növbəti "
              + "skanda üzünüz aydın görünsün — papaq və eynəyi çıxarın, işıqlı yerdə çəkin."
            : custom.Length > 500 ? custom[..500] : custom;;

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
            // The message text goes in the audit line, not just the fact that one was sent. A note
            // to a named person about their honesty is the kind of thing that gets asked about later,
            // and «bir mesaj göndərildi» answers nothing.
            Reason = $"Photo warning sent by {User.EmployeeId()}: {record.AttendanceDate:yyyy-MM-dd}, "
                   + $"face {record.FaceMatchStatus}"
                   + (record.FaceMatchScore is int sc ? $" {sc}%" : "")
                   + $" — \"{body}\"",
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
    [Permission(AccessPermissions.AttendanceFraud)]
    [HttpPost("{recordId:guid}/void-fraud")]
    public async Task<IActionResult> VoidFraud(
        Guid recordId,
        [FromBody] VoidFraudRequest request,
        [FromServices] IPushNotifier pushNotifier)
    {
        var ct = HttpContext.RequestAborted;
        // Required since 2026-10-15 like every correction; the old fallback wording («Giriş şəkli saxtadır»)
        // is now the screen's first preset, chosen by a person rather than filled in for them.
        if (CorrectionReason.Clean(request.Reason) is not { } voidReason)
            return BadRequest(new { error = CorrectionReason.Required });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Same boundary as every other write here: a manager reaches only their own branches' plain
        // employees. This one docks a day's pay and sends an accusation, so the check runs first.
        // Voiding or restoring a day changes what it pays — never on one's own (the owner, 2026-10-15).
        if (OwnDay(record.EmployeeId) is { } own)
            return own;
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
        record.VoidReason = voidReason;

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
                   // «Səbəb:» like every other correction's line, so one search finds them all.
                   + $". Səbəb: {record.VoidReason}",
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
    [Permission(AccessPermissions.AttendanceFraud)]
    [HttpPost("{recordId:guid}/unvoid")]
    public async Task<IActionResult> Unvoid(
        Guid recordId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttendanceCorrectionRequest? request)
    {
        var ct = HttpContext.RequestAborted;
        if (CorrectionReason.Clean(request?.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });
        // Voiding or restoring a day changes what it pays — never on one's own (the owner, 2026-10-15).
        if (OwnDay(record.EmployeeId) is { } own)
            return own;
        if (!await LocationScopeRules.CanManageEmployeeAsync(
                _db, User.EmployeeId(), User.Role(), record.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
        if (record.VoidedAtUtc is null)
            return Conflict(new { error = "NotVoided" });

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = record.EmployeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = Cap($"Void reversed by {User.EmployeeId()}: {record.AttendanceDate:yyyy-MM-dd} "
                         + $"(ləğvin səbəbi: {record.VoidReason}). Səbəb: {reason}"),
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
    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpDelete("{recordId:guid}")]
    public async Task<IActionResult> Delete(
        Guid recordId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttendanceCorrectionRequest? request)
    {
        // In the body, not the query string: a reason is free text about a person, and URLs end up in logs.
        if (CorrectionReason.Clean(request?.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Same boundary as the edit and the clear-checkout beside it: a manager reaches only their own
        // branches' plain employees. Deletion is not a wider power than editing a day to zero, and the
        // manager is who notices a phantom on their own board — but it is irreversible, so the scope
        // check runs before anything is touched. Never one's own day.
        if (OwnDay(record.EmployeeId) is { } own)
            return own;
        if (!await LocationScopeRules.CanCorrectAttendanceAsync(
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
            Reason = Cap($"Deleted by {requesterId}: {date:yyyy-MM-dd} {Times(record)}"
                         + (record.CheckInPhotoKey is null ? "" : $", photo {record.CheckInPhotoKey}")
                         + $". Səbəb: {reason}"),
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
    [Permission(AccessPermissions.AttendanceMaintenance)]
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
    [Permission(AccessPermissions.AttendanceMaintenance)]
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

    /// <summary>
    /// One audit line per corrected record: who, which record, what changed and — required since 2026-10-15 —
    /// why, in the corrector's own words.
    /// </summary>
    // ─────────────────────────── one's own day (docs/strukturlar-ve-selahiyyetler.md §18) ───────────────────────────

    /// <summary>
    /// Who may correct this day, and how. Somebody else's: today's rule (CanCorrectAttendanceAsync), refused with
    /// <paramref name="outOfScope"/>. One's own: only where nobody else in the company could (SelfCorrection) — at
    /// once, or waiting for approval — and otherwise refused with the names of who can, so the screen says whom to
    /// ask. Self is null for somebody else's day.
    /// </summary>
    private async Task<(IActionResult? Refusal, SelfCorrectionAnswer? Self)> CorrectorAsync(Guid employeeId, string outOfScope)
    {
        var ct = HttpContext.RequestAborted;
        if (employeeId == User.EmployeeId())
        {
            var verdict = await SelfCorrection.AskAsync(_db, employeeId, User.Role(), User.IsImpersonating(), ct);
            return verdict.Refused
                ? (StatusCode(StatusCodes.Status403Forbidden, new { error = "OwnAttendance", correctors = verdict.Correctors }), null)
                : (null, verdict.Answer);
        }
        if (!await LocationScopeRules.CanCorrectAttendanceAsync(_db, User.EmployeeId(), User.Role(), employeeId, ct))
            return (StatusCode(StatusCodes.Status403Forbidden, new { error = outOfScope }), null);
        return (null, null);
    }

    /// <summary>Sets a day's times — what an edit does now and what an approved self-correction does later — with the
    /// status recomputed the way the scan would have, so a corrected time cannot produce a status the person's own
    /// shift would never have given. Null leaves a time as it is. Not saved here.</summary>
    private async Task ApplyTimesAsync(AttendanceRecord record, Location location, DateTime? checkIn, DateTime? checkOut, Guid manualBy)
    {
        if (checkIn is not null)
        {
            record.CheckInAtUtc = checkIn;
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == record.EmployeeId);
            var shift = employee is null
                ? EffectiveShift.Resolve(null, null, null, 1, null, null, location)
                : EffectiveShift.Resolve(employee, await ScheduleForAsync(employee, record.AttendanceDate), location);
            record.Status = AttendanceController.DetermineStatus(
                shift.HoursOn(record.AttendanceDate).Start, shift.LateThresholdMinutes, checkIn.Value, _timeZone);
        }
        if (checkOut is not null)
            record.CheckOutAtUtc = checkOut;
        record.ManualByEmployeeId = manualBy; // this record was touched by hand — attribute it
    }

    /// <summary>A day written by hand for somebody who never scanned. Not added or saved here.</summary>
    private async Task<AttendanceRecord> NewRecordAsync(
        Employee employee, Location location, DateOnly date, DateTime checkIn, DateTime? checkOut, Guid manualBy) => new()
    {
        EmployeeId = employee.Id,
        LocationId = employee.LocationId,
        AttendanceDate = date,
        CheckInAtUtc = checkIn,
        CheckOutAtUtc = checkOut,
        ManualByEmployeeId = manualBy, // created by hand, not a scan
        Status = AttendanceController.DetermineStatus(
            EffectiveShift.Resolve(employee, await ScheduleForAsync(employee, date), location).HoursOn(date).Start,
            location.LateThresholdMinutes, checkIn, _timeZone),
    };

    /// <summary>
    /// The company's self-corrections — one's own, and those of the people whose days the caller may see —
    /// newest first, with whether each waits, how it was decided, and whether the caller may decide it. And the
    /// company's setting: does a self-correction wait for a second approval, and is there anybody to give it.
    /// </summary>
    [Permission(AccessPermissions.AttendanceView)]
    [HttpGet("self-corrections")]
    public async Task<IActionResult> SelfCorrections([FromQuery] int days = 90)
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 366));
        var all = await _db.AttendanceSelfCorrections.AsNoTracking()
            .Where(c => c.AtUtc >= since).OrderByDescending(c => c.AtUtc).Take(1000).ToListAsync(ct);
        var decisions = all.Where(c => c.DecisionForId != null).ToDictionary(c => c.DecisionForId!.Value);

        var subjects = all.Select(c => c.EmployeeId).Distinct().ToList();
        var seen = new HashSet<Guid>();
        foreach (var subject in subjects)
            if (subject == me || await LocationScopeRules.CanAccessEmployeeAsync(_db, me, User.Role(), subject, ct, AccessPermissions.AttendanceView))
                seen.Add(subject);
        var mayDecide = new Dictionary<Guid, bool>();
        foreach (var subject in seen.Where(s => s != me))
            mayDecide[subject] = !User.IsImpersonating() && (await SelfCorrection.ApproversAsync(_db, subject, ct)).Contains(me);

        var people = all.Select(c => c.EmployeeId).Concat(all.Select(c => c.ActorEmployeeId)).Distinct().ToList();
        var names = await _db.Employees.AsNoTracking().Where(e => people.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var tenant = await _db.Tenants.AsNoTracking().Where(t => t.Id == _db.CurrentTenantId)
            .Select(t => new { t.SelfCorrectionNeedsApproval }).FirstOrDefaultAsync(ct);

        return Ok(new
        {
            needsApproval = tenant?.SelfCorrectionNeedsApproval ?? false,
            // Whether this caller could switch the second approval on: somebody would have to give it.
            approverAvailable = (await SelfCorrection.ApproversAsync(_db, me, ct)).Count > 0,
            items = all.Where(c => c.DecisionForId == null && seen.Contains(c.EmployeeId)).Select(c =>
            {
                decisions.TryGetValue(c.Id, out var decision);
                return new
                {
                    id = c.Id,
                    employeeId = c.EmployeeId,
                    employeeName = names.GetValueOrDefault(c.EmployeeId, "?"),
                    date = c.AttendanceDate,
                    kind = c.Kind,
                    checkInBeforeUtc = c.CheckInBeforeUtc,
                    checkOutBeforeUtc = c.CheckOutBeforeUtc,
                    checkInAfterUtc = c.CheckInAfterUtc,
                    checkOutAfterUtc = c.CheckOutAfterUtc,
                    reason = c.Reason,
                    atUtc = c.AtUtc,
                    awaitsApproval = c.AwaitsApproval && decision is null,
                    decision = decision is null ? null : new
                    {
                        approved = decision.Kind == SelfCorrection.Approved,
                        byName = names.GetValueOrDefault(decision.ActorEmployeeId, "?"),
                        reason = decision.Reason,
                        atUtc = decision.AtUtc,
                    },
                    mayDecide = c.AwaitsApproval && decision is null && mayDecide.GetValueOrDefault(c.EmployeeId),
                };
            }),
        });
    }

    /// <summary>
    /// The answer to a self-correction that waited (the company asked for a second approval). Approved: applied
    /// now, exactly as the person asked — if the day is still what they saw; a day changed meanwhile is refused,
    /// never overwritten. Rejected: nothing changes. Either way a decision row of its own and an audit line. Only
    /// somebody the company counts as an approver (SelfCorrection.ApproversAsync), never the person themselves.
    /// </summary>
    // Today an admin's (the approvers are the other admins); in Enforced whoever holds the approval right, the
    // action narrowing itself to the people they may approve for (Scoped).
    [Permission(AccessPermissions.AttendanceSelfCorrectionApprove, Scoped = true)]
    [Authorize(Roles = "Admin")]
    [HttpPost("self-corrections/{id:guid}/decide")]
    public async Task<IActionResult> DecideSelfCorrection(Guid id, [FromBody] SelfCorrectionDecisionRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (CorrectionReason.Clean(request.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });
        var row = await _db.AttendanceSelfCorrections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.AwaitsApproval, ct);
        if (row is null)
            return NotFound(new { error = "RequestNotFound" });
        var me = User.EmployeeId();
        if (row.EmployeeId == me || User.IsImpersonating())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OwnAttendance" });
        if (!(await SelfCorrection.ApproversAsync(_db, row.EmployeeId, ct)).Contains(me))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotAnApprover" });
        if (await _db.AttendanceSelfCorrections.AnyAsync(c => c.DecisionForId == id, ct))
            return Conflict(new { error = "AlreadyReviewed" });

        Guid? recordId = row.RecordId;
        if (request.Approve)
        {
            if (row.Kind == SelfCorrection.Create)
            {
                if (await _db.AttendanceRecords.AnyAsync(r => r.EmployeeId == row.EmployeeId && r.AttendanceDate == row.AttendanceDate, ct))
                    return Conflict(new { error = "RecordAlreadyExists" });
                var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.EmployeeId, ct);
                var home = employee is null ? null : await _db.Locations.FirstOrDefaultAsync(l => l.Id == employee.LocationId, ct);
                if (employee is null || home is null || row.CheckInAfterUtc is not DateTime checkIn)
                    return BadRequest(new { error = "EmployeeNotFound" });
                var created = await NewRecordAsync(employee, home, row.AttendanceDate, checkIn, row.CheckOutAfterUtc, row.EmployeeId);
                _db.AttendanceRecords.Add(created);
                recordId = created.Id;
            }
            else
            {
                var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == row.RecordId, ct);
                if (record is null)
                    return NotFound(new { error = "RecordNotFound" });
                // The day as the person saw it when they asked, or nothing: never an overwrite of a later change.
                if (record.CheckInAtUtc != row.CheckInBeforeUtc || record.CheckOutAtUtc != row.CheckOutBeforeUtc)
                    return Conflict(new { error = "RecordChanged" });
                if (row.Kind == SelfCorrection.ClearCheckout)
                {
                    record.CheckOutAtUtc = null;
                    record.ManualByEmployeeId = row.EmployeeId;
                }
                else
                {
                    var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == record.LocationId, ct);
                    if (location is null)
                        return BadRequest(new { error = "LocationNotFound" });
                    await ApplyTimesAsync(record, location,
                        row.CheckInAfterUtc != row.CheckInBeforeUtc ? row.CheckInAfterUtc : null, row.CheckOutAfterUtc, row.EmployeeId);
                }
            }
        }

        _db.AttendanceSelfCorrections.Add(new AttendanceSelfCorrection
        {
            TenantId = _db.CurrentTenantId, EmployeeId = row.EmployeeId, ActorEmployeeId = me, RecordId = recordId,
            AttendanceDate = row.AttendanceDate, Kind = request.Approve ? SelfCorrection.Approved : SelfCorrection.Rejected,
            CheckInBeforeUtc = row.CheckInBeforeUtc, CheckOutBeforeUtc = row.CheckOutBeforeUtc,
            CheckInAfterUtc = row.CheckInAfterUtc, CheckOutAfterUtc = row.CheckOutAfterUtc,
            Reason = reason, DecisionForId = row.Id,
        });
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = row.EmployeeId,
            EventType = AuditEventType.SelfCorrectionDecided,
            Reason = Cap($"{(request.Approve ? "Təsdiqləndi" : "Rədd edildi")} — by {me}: {row.Kind} {row.AttendanceDate:yyyy-MM-dd} "
                         + $"{SelfCorrection.Times(row.CheckInBeforeUtc, row.CheckOutBeforeUtc)} → {SelfCorrection.Times(row.CheckInAfterUtc, row.CheckOutAfterUtc)}. Səbəb: {reason}"),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await _db.SaveChangesAsync(ct);
        if (request.Approve)
            await _dailySummaryService.GenerateForDateAsync(row.AttendanceDate, ct);
        return Ok(new { status = request.Approve ? "Approved" : "Rejected" });
    }

    /// <summary>
    /// Switches «a self-correction waits for a second approval» on or off for the company. On only where somebody
    /// could give that approval — a setting nobody can satisfy would only freeze the admin's own days. Audited.
    /// </summary>
    [Permission(AccessPermissions.AttendanceCorrect, AtCompany = true)]
    [Authorize(Roles = "Admin")]
    [HttpPut("self-corrections/setting")]
    public async Task<IActionResult> SetSelfCorrectionSetting([FromBody] SelfCorrectionSettingRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        if (request.NeedsApproval && (await SelfCorrection.ApproversAsync(_db, me, ct)).Count == 0)
            return Conflict(new { error = "NoApprover" });
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _db.CurrentTenantId, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });
        if (tenant.SelfCorrectionNeedsApproval != request.NeedsApproval)
        {
            tenant.SelfCorrectionNeedsApproval = request.NeedsApproval;
            _db.AuditLogs.Add(new AuditLog
            {
                EmployeeId = me,
                EventType = AuditEventType.SelfCorrectionSettingChanged,
                Reason = $"By {me}: öz düzəlişi üçün ikinci təsdiq {(request.NeedsApproval ? "açıldı" : "bağlandı")}",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new { needsApproval = tenant.SelfCorrectionNeedsApproval });
    }

    private async Task WriteAuditAsync(Guid employeeId, Guid requesterId, Guid recordId, string? ip, string what, string reason)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = Cap($"By {requesterId}, record {recordId}: {what}. Səbəb: {reason}"),
            IpAddress = ip
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>The 403 for a correction to one's own day, or null when the day is somebody else's. Nobody —
    /// an admin included — corrects the day they are paid for (the owner, 2026-10-15).</summary>
    private IActionResult? OwnDay(Guid employeeId) =>
        employeeId == User.EmployeeId()
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "OwnAttendance" })
            : null;

    private static string Times(AttendanceRecord r) =>
        $"{r.CheckInAtUtc:HH:mm}–{(r.CheckOutAtUtc is null ? "—" : r.CheckOutAtUtc.Value.ToString("HH:mm"))} UTC";

    /// <summary>The audit column holds 1000 characters; a long reason is cut, never refused at save time.</summary>
    private static string Cap(string text) => text.Length <= 1000 ? text : text[..1000];

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
    /// <summary>
    /// The schedule that answers for this employee ON THIS DATE — the day's «əvəzləmə» when there is
    /// one, otherwise their own. An admin correcting a cover night has to recompute it against the
    /// shift that night was actually worked under, or the correction re-states the error it is fixing.
    /// </summary>
    private async Task<Schedule?> ScheduleForAsync(Employee employee, DateOnly date)
    {
        var ct = HttpContext.RequestAborted;
        var covering = await _db.ShiftOverrides
            .Where(o => o.EmployeeId == employee.Id && o.Date == date)
            .Select(o => (Guid?)o.ScheduleId)
            .FirstOrDefaultAsync(ct);

        var id = covering ?? employee.ScheduleId;
        return id is Guid sid ? await _db.Schedules.FirstOrDefaultAsync(sc => sc.Id == sid, ct) : null;
    }
}
