using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The person's side of presence checks («Mövcudluq yoxlaması», see <see cref="Domain.Entities.PresenceCheck"/>):
/// is there a question waiting for me, and here is where I am.
///
/// Only ever the caller's own checks (the tenant filter plus <c>EmployeeId == me</c>), and nothing here touches the
/// day itself: an answer is written to the check and nowhere else. A check-in is never blocked by anything
/// optional, and this is optional.
/// </summary>
[ApiController]
[Authorize]
[Route("api/presence-checks")]
public class PresenceChecksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IStaffAlertQueue? _alerts;
    private readonly TimeProvider _clock;

    public PresenceChecksController(AppDbContext db, TimeProvider clock, IStaffAlertQueue? alerts = null)
    {
        _db = db;
        _clock = clock;
        _alerts = alerts;
    }

    // GET /api/presence-checks/pending — the question waiting for an answer, or { check: null }.
    //
    // The home page asks this on every open and once a minute while it is in front of the person, because the push
    // does not reach everyone: on an iPhone it arrives only when the app is installed to the home screen, and most
    // are not. Late answers are still taken, so a check stays here until the day closes.
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        var now = _clock.GetUtcNow().UtcDateTime;
        var oldest = now.AddHours(-PresenceCheckRules.LongestShift.TotalHours);

        var check = await (
            from c in _db.PresenceChecks
            join r in _db.AttendanceRecords on c.AttendanceRecordId equals r.Id
            where c.EmployeeId == me && c.SentAtUtc != null && c.SentAtUtc >= oldest
                  && (c.Result == PresenceCheckResult.Pending || c.Result == PresenceCheckResult.NoAnswer
                      || c.Result == PresenceCheckResult.GpsFailed)
                  && r.CheckOutAtUtc == null && r.VoidedAtUtc == null
            orderby c.SentAtUtc descending
            select new { c.Id, c.SentAtUtc })
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            check = check is null ? null : new
            {
                id = check.Id,
                sentAtUtc = check.SentAtUtc,
                answerByUtc = check.SentAtUtc!.Value + PresenceCheckRules.AnswerWindow,
            },
        });
    }

    // POST /api/presence-checks/{id}/answer — the phone's position, or why there is none.
    [HttpPost("{id:guid}/answer")]
    public async Task<IActionResult> Answer(Guid id, [FromBody] PresenceCheckAnswerRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var me = User.EmployeeId();
        var now = _clock.GetUtcNow().UtcDateTime;

        var check = await _db.PresenceChecks.FirstOrDefaultAsync(c => c.Id == id && c.EmployeeId == me, ct);
        // Not asked yet is the same as not there: the moment of a planned check is never told to anyone.
        if (check is null || check.SentAtUtc is null)
            return NotFound(new { error = "PresenceCheckNotFound" });

        // Inside and outside are final. A second answer — a retry, a second tab — gets the first one's result back.
        if (!PresenceCheckRules.IsAnswerable(check.Result))
            return Conflict(new { error = "AlreadyAnswered", result = check.Result.ToString() });

        var day = await _db.AttendanceRecords.AsNoTracking()
            .Where(r => r.Id == check.AttendanceRecordId)
            .Select(r => new { r.CheckOutAtUtc, r.VoidedAtUtc })
            .FirstOrDefaultAsync(ct);
        // After the day is closed the question no longer means anything: where they are now is not where they were.
        if (day is null || day.CheckOutAtUtc != null || day.VoidedAtUtc != null)
            return Conflict(new { error = "DayClosed" });

        var late = PresenceCheckRules.IsLate(check.SentAtUtc.Value, now);

        if (!string.IsNullOrWhiteSpace(request.Failure))
        {
            var failure = request.Failure.Trim();
            if (!PresenceCheckRules.GpsFailureKinds.Contains(failure, StringComparer.Ordinal))
                return BadRequest(new { error = "UnknownFailure" });
            // «GPS alınmadı», never «outside»: the phone said nothing about where they are. They may try again.
            check.Result = PresenceCheckResult.GpsFailed;
            check.Note = failure;
            check.RespondedAtUtc = now;
            check.Late = late;
            await _db.SaveChangesAsync(ct);
            return Ok(new { result = check.Result.ToString(), late });
        }

        if (request.Latitude is not double lat || request.Longitude is not double lng
            || !PresenceCheckRules.IsValidFix(lat, lng, request.AccuracyMeters))
            return BadRequest(new { error = "InvalidPosition" });

        var branch = await _db.Locations.AsNoTracking()
            .Where(l => l.Id == check.LocationId)
            .Select(l => new { l.Latitude, l.Longitude, l.RadiusMeters })
            .FirstOrDefaultAsync(ct);
        if (branch is null)
            return Conflict(new { error = "BranchNotFound" });

        var distance = GeoCalculator.DistanceMeters(branch.Latitude, branch.Longitude, lat, lng);
        var inside = PresenceCheckRules.IsInside(distance, branch.RadiusMeters);

        check.Result = inside ? PresenceCheckResult.Inside : PresenceCheckResult.Outside;
        check.Note = null;
        check.RespondedAtUtc = now;
        check.Late = late;
        check.Latitude = lat;
        check.Longitude = lng;
        check.AccuracyMeters = request.AccuracyMeters;
        check.DistanceMeters = double.IsFinite(distance) ? Math.Round(distance, 1) : null;
        check.RadiusMeters = branch.RadiusMeters;
        await _db.SaveChangesAsync(ct);

        // The one result a manager hears about, through the same dispatcher as every other staff alert.
        if (!inside)
            _alerts?.Enqueue(new StaffAlert(_db.CurrentTenantId, me, StaffAlertKinds.PresenceOutside, now));

        // The screen says «qeyd olundu» either way; the result is here for the record, not for the phone to argue with.
        return Ok(new { result = check.Result.ToString(), late });
    }
}
