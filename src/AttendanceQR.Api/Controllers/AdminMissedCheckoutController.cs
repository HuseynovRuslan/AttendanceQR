using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Manager/Admin side of the forgot-to-check-out flow: review pending requests (scoped to the
/// manager's locations), then approve — which writes the record's CheckOutAtUtc and recomputes that
/// day's summary — or reject. Each row shows the employee's this-month count, so a repeat forgetter is
/// visible to whoever approves.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/missed-checkout")]
public class AdminMissedCheckoutController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _dailySummaryService;

    public AdminMissedCheckoutController(AppDbContext db, IDailySummaryService dailySummaryService)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
    }

    [Permission(AccessPermissions.AttendanceView)]
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var callerId = User.EmployeeId();
        var role = User.Role();

        var ct = HttpContext.RequestAborted;

        // Managers see only their locations; admins see everything.
        List<Guid>? managed = role == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, callerId, ct)
            : null;

        var query =
            from r in _db.MissedCheckoutRequests
            where r.Status == MissedCheckoutStatus.Pending
            join e in _db.Employees on r.EmployeeId equals e.Id
            join l in _db.Locations on e.LocationId equals l.Id
            // A manager reviews only their branches' Role==Employee requests — not a same-branch
            // admin's/manager's, and not their own (self-approval is no review; an admin closes theirs).
            where managed == null || (managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee && e.Id != callerId)
            orderby r.RequestedAtUtc
            select new
            {
                id = r.Id,
                employeeId = e.Id,
                employeeName = e.FullName,
                locationName = l.Name,
                attendanceDate = r.AttendanceDate,
                requestedCheckOutAtUtc = r.RequestedCheckOutAtUtc,
                reason = r.Reason,
                requestedAtUtc = r.RequestedAtUtc
            };

        var rows = await query.Take(500).ToListAsync(ct);

        // This-month count per employee shown in the list — the visibility deterrent.
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var ids = rows.Select(r => r.employeeId).Distinct().ToList();
        var counts = await _db.MissedCheckoutRequests
            .Where(r => ids.Contains(r.EmployeeId) && r.RequestedAtUtc >= monthStart
                        && r.Status != MissedCheckoutStatus.Rejected)
            .GroupBy(r => r.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, ct);

        var result = rows.Select(r => new
        {
            r.id,
            r.employeeName,
            r.locationName,
            r.attendanceDate,
            r.requestedCheckOutAtUtc,
            r.reason,
            r.requestedAtUtc,
            monthlyCount = counts.GetValueOrDefault(r.employeeId, 0)
        });

        return Ok(result);
    }

    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttendanceCorrectionRequest? request)
    {
        var callerId = User.EmployeeId();
        var role = User.Role();
        var ct = HttpContext.RequestAborted;
        if (CorrectionReason.Clean(request?.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var mc = await _db.MissedCheckoutRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (mc is null)
            return NotFound(new { error = "RequestNotFound" });
        if (mc.Status != MissedCheckoutStatus.Pending)
            return Conflict(new { error = "AlreadyReviewed" });

        // Approving WRITES the target's checkout, so this is the manage rule, not mere visibility:
        // a manager may close only their own branches' Role==Employee days — never an admin's, a
        // fellow manager's, or (unlike CanAccess) their OWN request, which would be self-approval.
        // Never one's own request: deciding it is correcting one's own day (the owner, 2026-10-15) — unless
        // nobody else in the company could decide it (SelfCorrection, the single-admin rule of 2026-10-16).
        var own = mc.EmployeeId == callerId;
        if (own)
        {
            var verdict = await SelfCorrection.AskAsync(_db, callerId, role, User.IsImpersonating(), ct);
            if (verdict.Refused)
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "OwnAttendance", correctors = verdict.Correctors });
            // A second approval is asked for the day itself: the record's own edit sends it there.
            if (verdict.Answer == SelfCorrectionAnswer.AwaitApproval)
                return Conflict(new { error = "SelfCorrectionNeedsApproval" });
        }
        else if (!await LocationScopeRules.CanCorrectAttendanceAsync(_db, callerId, role, mc.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == mc.AttendanceRecordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Only write the checkout if the day is still open (an admin may have closed it in the meantime).
        var beforeOut = record.CheckOutAtUtc;
        if (record.CheckOutAtUtc is null)
            record.CheckOutAtUtc = mc.RequestedCheckOutAtUtc;

        mc.Status = MissedCheckoutStatus.Approved;
        mc.ReviewedByEmployeeId = callerId;
        mc.ReviewedAtUtc = DateTime.UtcNow;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (own)
            SelfCorrection.Record(_db, mc.EmployeeId, record.Id, record.AttendanceDate, SelfCorrection.MissedCheckout,
                record.CheckInAtUtc, beforeOut, record.CheckInAtUtc, record.CheckOutAtUtc, reason, awaitsApproval: false, ip);
        else
            _db.AuditLogs.Add(CorrectionReason.Audit(mc.EmployeeId, callerId, ip,
                $"Unudulmuş çıxış təsdiqləndi, record {record.Id}: {record.AttendanceDate:yyyy-MM-dd} çıxış "
                + $"{mc.RequestedCheckOutAtUtc:HH:mm} UTC (işçinin səbəbi: {mc.Reason})", reason));
        await _db.SaveChangesAsync(ct);

        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, ct);

        return Ok(new { status = "Approved" });
    }

    [Permission(AccessPermissions.AttendanceCorrect)]
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AttendanceCorrectionRequest? request)
    {
        var callerId = User.EmployeeId();
        var role = User.Role();
        var ct = HttpContext.RequestAborted;
        if (CorrectionReason.Clean(request?.Reason) is not { } reason)
            return BadRequest(new { error = CorrectionReason.Required });

        var mc = await _db.MissedCheckoutRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (mc is null)
            return NotFound(new { error = "RequestNotFound" });
        if (mc.Status != MissedCheckoutStatus.Pending)
            return Conflict(new { error = "AlreadyReviewed" });

        // Same manage rule as Approve — rejecting is also a review decision over the target's day.
        // Never one's own request: deciding it is correcting one's own day (the owner, 2026-10-15) — unless
        // nobody else could decide it; withdrawing it changes no time, so it waits for nobody.
        if (mc.EmployeeId == callerId)
        {
            var verdict = await SelfCorrection.AskAsync(_db, callerId, role, User.IsImpersonating(), ct);
            if (verdict.Refused)
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "OwnAttendance", correctors = verdict.Correctors });
        }
        else if (!await LocationScopeRules.CanCorrectAttendanceAsync(_db, callerId, role, mc.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        mc.Status = MissedCheckoutStatus.Rejected;
        mc.ReviewedByEmployeeId = callerId;
        mc.ReviewedAtUtc = DateTime.UtcNow;
        _db.AuditLogs.Add(CorrectionReason.Audit(mc.EmployeeId, callerId, HttpContext.Connection.RemoteIpAddress?.ToString(),
            $"Unudulmuş çıxış rədd edildi, record {mc.AttendanceRecordId} (işçinin səbəbi: {mc.Reason})", reason));
        await _db.SaveChangesAsync(ct);

        return Ok(new { status = "Rejected" });
    }

}
