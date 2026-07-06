using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
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
[Authorize(Roles = "Admin")]
[Route("api/admin/attendance")]
public class AdminAttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _dailySummaryService;

    public AdminAttendanceController(AppDbContext db, IDailySummaryService dailySummaryService)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
    }

    [HttpPut("{recordId:guid}")]
    public async Task<IActionResult> Update(Guid recordId, [FromBody] AdminAttendanceUpdateRequest request)
    {
        if (request.CheckInAtUtc is null && request.CheckOutAtUtc is null)
            return BadRequest(new { error = "NothingToUpdate" });

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

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
            record.Status = AttendanceController.DetermineStatus(location, request.CheckInAtUtc.Value);
        }
        if (request.CheckOutAtUtc is not null)
            record.CheckOutAtUtc = request.CheckOutAtUtc;

        await _db.SaveChangesAsync();

        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            requesterId = Guid.Empty;
        await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

        await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, HttpContext.RequestAborted);

        return Ok(Project(record));
    }

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

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == employee.LocationId);
        if (location is null)
            return BadRequest(new { error = "LocationNotFound" });

        if (await _db.AttendanceRecords.AnyAsync(r => r.EmployeeId == request.EmployeeId && r.AttendanceDate == request.Date))
            return Conflict(new { error = "RecordAlreadyExists" });

        var record = new AttendanceRecord
        {
            EmployeeId = request.EmployeeId,
            LocationId = employee.LocationId,
            AttendanceDate = request.Date,
            CheckInAtUtc = request.CheckInAtUtc,
            CheckOutAtUtc = request.CheckOutAtUtc,
            Status = AttendanceController.DetermineStatus(location, request.CheckInAtUtc)
        };
        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync();

        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            requesterId = Guid.Empty;
        await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, HttpContext.Connection.RemoteIpAddress?.ToString());

        await _dailySummaryService.GenerateForDateAsync(request.Date, HttpContext.RequestAborted);

        return Ok(Project(record));
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
}
