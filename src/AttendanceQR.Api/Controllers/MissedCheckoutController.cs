using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Employee side of the "forgot to check out" flow. The home banner asks here whether the caller has a
/// past day left open; if so, they submit the time they actually left. It becomes a request a
/// manager/admin approves — the employee never writes their own hours. Deterrents: a required reason
/// and a per-calendar-month cap so this stays a safety net, not a habit.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/missed-checkout")]
public class MissedCheckoutController : ControllerBase
{
    private readonly AppDbContext _db;

    // Per-calendar-month cap on self-reports before the employee must go through the admin directly.
    public const int MonthlyLimit = 3;

    public MissedCheckoutController(AppDbContext db) => _db = db;

    // GET /api/attendance/missed-checkout — for the home banner: the oldest open past day (if any),
    // this month's self-report count, the cap, and whether a request is already pending for that day.
    [HttpGet]
    public async Task<IActionResult> Status()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

        var open = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.CheckInAtUtc != null
                        && r.CheckOutAtUtc == null && r.AttendanceDate < todayUtc)
            .OrderByDescending(r => r.AttendanceDate)
            .Select(r => new { r.Id, r.AttendanceDate, r.CheckInAtUtc })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthlyCount = await CountThisMonthAsync(employeeId, monthStart, HttpContext.RequestAborted);

        var pending = open is not null && await _db.MissedCheckoutRequests.AnyAsync(
            r => r.AttendanceRecordId == open.Id && r.Status == MissedCheckoutStatus.Pending,
            HttpContext.RequestAborted);

        return Ok(new
        {
            openDay = open is null ? null : new
            {
                recordId = open.Id,
                attendanceDate = open.AttendanceDate,
                checkInAtUtc = open.CheckInAtUtc
            },
            monthlyCount,
            limit = MonthlyLimit,
            pending
        });
    }

    // POST /api/attendance/missed-checkout — submit the claimed check-out time for a forgotten day.
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] MissedCheckoutRequestBody request)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "ReasonRequired" });
        var reason = request.Reason.Trim();
        if (reason.Length > 300)
            reason = reason[..300];

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(
            r => r.Id == request.RecordId && r.EmployeeId == employeeId, HttpContext.RequestAborted);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });
        if (record.CheckInAtUtc is null)
            return BadRequest(new { error = "NoCheckIn" });
        if (record.CheckOutAtUtc is not null)
            return Conflict(new { error = "AlreadyClosed" });
        if (record.AttendanceDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            return BadRequest(new { error = "NotPastDay" });

        var checkOut = request.CheckOutAtUtc;
        if (checkOut > DateTime.UtcNow)
            return BadRequest(new { error = "CheckOutInFuture" });
        if (checkOut <= record.CheckInAtUtc.Value)
            return BadRequest(new { error = "CheckOutBeforeCheckIn" });

        // Cap reached — the employee must talk to the admin instead of self-reporting again.
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        if (await CountThisMonthAsync(employeeId, monthStart, HttpContext.RequestAborted) >= MonthlyLimit)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "MonthlyLimitReached" });

        if (await _db.MissedCheckoutRequests.AnyAsync(
                r => r.AttendanceRecordId == record.Id && r.Status == MissedCheckoutStatus.Pending,
                HttpContext.RequestAborted))
            return Conflict(new { error = "AlreadyRequested" });

        var mc = new MissedCheckoutRequest
        {
            EmployeeId = employeeId,
            AttendanceRecordId = record.Id,
            AttendanceDate = record.AttendanceDate,
            RequestedCheckOutAtUtc = checkOut,
            Reason = reason,
            Status = MissedCheckoutStatus.Pending
        };
        _db.MissedCheckoutRequests.Add(mc);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        return StatusCode(StatusCodes.Status201Created, new { id = mc.Id });
    }

    // Requests this calendar month that "used" the path — pending or approved (a rejected one isn't
    // counted against the employee).
    private Task<int> CountThisMonthAsync(Guid employeeId, DateTime monthStart, CancellationToken ct)
        => _db.MissedCheckoutRequests.CountAsync(
            r => r.EmployeeId == employeeId && r.RequestedAtUtc >= monthStart
                 && r.Status != MissedCheckoutStatus.Rejected, ct);
}
