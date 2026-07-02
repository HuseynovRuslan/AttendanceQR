using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
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
    private readonly AppDbContext _db;
    private readonly IQrTokenService _qrTokenService;
    private readonly INonceStore _nonceStore;
    private readonly IAttendanceQueryService _attendanceQuery;

    public AttendanceController(
        AppDbContext db,
        IQrTokenService qrTokenService,
        INonceStore nonceStore,
        IAttendanceQueryService attendanceQuery)
    {
        _db = db;
        _qrTokenService = qrTokenService;
        _nonceStore = nonceStore;
        _attendanceQuery = attendanceQuery;
    }

    // GET /api/attendance/me — the caller's own records. Identity is the JWT "sub"; there is no
    // way to ask for anyone else's here.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        var records = await _attendanceQuery.GetOwnRecordsAsync(employeeId, HttpContext.RequestAborted);
        return Ok(records);
    }

    // GET /api/attendance/employee/{id} — another employee's records, subject to a resource-level
    // check in the service ([Authorize] alone cannot enforce "only your own / your team's").
    [HttpGet("employee/{employeeId:guid}")]
    public async Task<IActionResult> ForEmployee(Guid employeeId)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            return Unauthorized(new { error = "InvalidToken" });

        if (!Enum.TryParse<EmployeeRole>(User.FindFirstValue("role"), out var role))
            return Unauthorized(new { error = "InvalidToken" });

        var (access, records) = await _attendanceQuery.GetForEmployeeAsync(
            employeeId, requesterId, role, HttpContext.RequestAborted);

        if (access == AttendanceAccess.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return Ok(records);
    }

    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] ScanRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Identity comes from the authenticated JWT ("sub" claim), never from the body.
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        // 1. QR token validity (signature/format/expiry — all server-side).
        var validation = _qrTokenService.Validate(request.QrToken);
        if (!validation.IsValid)
        {
            await WriteAuditAsync(employeeId, AuditEventType.CheckInRejected, validation.FailureReason, ip);
            return BadRequest(new { error = validation.FailureReason });
        }

        // 2. Replay protection — a token's nonce may be consumed exactly once.
        if (!_nonceStore.TryConsume(validation.Nonce!))
        {
            await WriteAuditAsync(employeeId, AuditEventType.CheckInRejected, "TokenReused", ip);
            return BadRequest(new { error = "TokenReused" });
        }

        // 3. Employee must exist and be active.
        var employee = await _db.Employees
            .Include(e => e.DeviceBinding)
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
        if (employee is null)
        {
            await WriteAuditAsync(null, AuditEventType.CheckInRejected, "EmployeeNotFoundOrInactive", ip);
            return Unauthorized(new { error = "EmployeeNotFoundOrInactive" });
        }

        // 4. Device binding is now mandatory — every activated employee has a bound device.
        if (employee.DeviceBinding is null)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "NoDeviceBound", ip);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NoDeviceBound" });
        }
        if (!string.Equals(employee.DeviceBinding.DeviceFingerprint, request.DeviceFingerprint, StringComparison.Ordinal))
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "DeviceMismatch", ip);
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "DeviceMismatch" });
        }

        // 5. Geofence — must be within the location's radius (token carries the LocationId).
        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == validation.LocationId!.Value);
        if (location is null)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "LocationNotFound", ip);
            return BadRequest(new { error = "LocationNotFound" });
        }

        var distanceMeters = GeoCalculator.DistanceMeters(
            request.Latitude, request.Longitude, location.Latitude, location.Longitude);
        if (distanceMeters > location.RadiusMeters)
        {
            await WriteAuditAsync(employee.Id, AuditEventType.CheckInRejected, "OutsideRadius", ip);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "OutsideRadius", distanceMeters = Math.Round(distanceMeters) });
        }

        // 6. Resolve today's record (server UTC day) and decide check-in vs check-out.
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);

        var record = await _db.AttendanceRecords
            .FirstOrDefaultAsync(r => r.EmployeeId == employee.Id && r.AttendanceDate == today);

        if (record is null)
            return await CheckInAsync(employee.Id, location, today, nowUtc, ip);

        if (record.CheckOutAtUtc is null)
            return await CheckOutAsync(record, nowUtc, employee.Id, ip);

        // Already checked in and out today.
        await WriteAuditAsync(employee.Id, AuditEventType.CheckOutRejected, "AlreadyCompleted", ip);
        return Conflict(new { error = "AlreadyCompleted" });
    }

    private async Task<IActionResult> CheckInAsync(
        Guid employeeId, Location location, DateOnly today, DateTime nowUtc, string? ip)
    {
        var record = new AttendanceRecord
        {
            EmployeeId = employeeId,
            LocationId = location.Id,
            AttendanceDate = today,
            CheckInAtUtc = nowUtc,
            Status = DetermineStatus(location, nowUtc)
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
            await WriteAuditAsync(employeeId, AuditEventType.CheckInRejected, "DuplicateCheckIn", ip);
            return Conflict(new { error = "DuplicateCheckIn" });
        }

        await WriteAuditAsync(employeeId, AuditEventType.CheckInSuccess, null, ip);
        return Ok(new
        {
            action = "CheckIn",
            recordId = record.Id,
            status = record.Status.ToString(),
            checkInAtUtc = nowUtc
        });
    }

    private async Task<IActionResult> CheckOutAsync(
        AttendanceRecord record, DateTime nowUtc, Guid employeeId, string? ip)
    {
        record.CheckOutAtUtc = nowUtc;
        await _db.SaveChangesAsync();

        await WriteAuditAsync(employeeId, AuditEventType.CheckOutSuccess, null, ip);
        return Ok(new
        {
            action = "CheckOut",
            recordId = record.Id,
            checkOutAtUtc = nowUtc
        });
    }

    /// <summary>
    /// OnTime unless the current time is past ShiftStart + LateThresholdMinutes.
    /// Note: shift times and server time are treated as the same reference here; a real
    /// deployment would carry a per-location timezone.
    /// </summary>
    private static AttendanceStatus DetermineStatus(Location location, DateTime nowUtc)
    {
        var lateCutoff = location.ShiftStart.AddMinutes(location.LateThresholdMinutes);
        var nowTime = TimeOnly.FromDateTime(nowUtc);
        return nowTime > lateCutoff ? AttendanceStatus.Late : AttendanceStatus.OnTime;
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
