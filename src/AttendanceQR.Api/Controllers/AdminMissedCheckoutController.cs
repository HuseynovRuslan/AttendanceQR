using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
        var callerId = User.EmployeeId();
        var role = User.Role();
        var ct = HttpContext.RequestAborted;

        var mc = await _db.MissedCheckoutRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (mc is null)
            return NotFound(new { error = "RequestNotFound" });
        if (mc.Status != MissedCheckoutStatus.Pending)
            return Conflict(new { error = "AlreadyReviewed" });

        // Approving WRITES the target's checkout, so this is the manage rule, not mere visibility:
        // a manager may close only their own branches' Role==Employee days — never an admin's, a
        // fellow manager's, or (unlike CanAccess) their OWN request, which would be self-approval.
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, callerId, role, mc.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == mc.AttendanceRecordId, ct);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        // Only write the checkout if the day is still open (an admin may have closed it in the meantime).
        if (record.CheckOutAtUtc is null)
            record.CheckOutAtUtc = mc.RequestedCheckOutAtUtc;

        mc.Status = MissedCheckoutStatus.Approved;
        mc.ReviewedByEmployeeId = callerId;
        mc.ReviewedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, ct);

        return Ok(new { status = "Approved" });
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
    {
        var callerId = User.EmployeeId();
        var role = User.Role();
        var ct = HttpContext.RequestAborted;

        var mc = await _db.MissedCheckoutRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (mc is null)
            return NotFound(new { error = "RequestNotFound" });
        if (mc.Status != MissedCheckoutStatus.Pending)
            return Conflict(new { error = "AlreadyReviewed" });

        // Same manage rule as Approve — rejecting is also a review decision over the target's day.
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, callerId, role, mc.EmployeeId, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        mc.Status = MissedCheckoutStatus.Rejected;
        mc.ReviewedByEmployeeId = callerId;
        mc.ReviewedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { status = "Rejected" });
    }

}
