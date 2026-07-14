using System.Security.Claims;
using AttendanceQR.Api.Contracts;
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
[Authorize(Roles = "Admin")]
[Route("api/admin/attendance")]
public class AdminAttendanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDailySummaryService _dailySummaryService;
    private readonly IFaceMatchQueue _faceQueue;

    public AdminAttendanceController(AppDbContext db, IDailySummaryService dailySummaryService, IFaceMatchQueue faceQueue)
    {
        _db = db;
        _dailySummaryService = dailySummaryService;
        _faceQueue = faceQueue;
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

        var rows = await (
            from r in _db.AttendanceRecords
            where r.CheckInAtUtc != null && r.CheckOutAtUtc == null && r.AttendanceDate < todayUtc
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
            var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == record.EmployeeId);
            var shiftStart = employee is null
                ? location.ShiftStart
                : AttendanceController.EffectiveShiftStart(employee, location);
            record.Status = AttendanceController.DetermineStatus(shiftStart, location.LateThresholdMinutes, request.CheckInAtUtc.Value);
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
            Status = AttendanceController.DetermineStatus(
                AttendanceController.EffectiveShiftStart(employee, location), location.LateThresholdMinutes, request.CheckInAtUtc)
        };
        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync();

        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            requesterId = Guid.Empty;
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

        if (record.CheckOutAtUtc is not null)
        {
            record.CheckOutAtUtc = null;
            await _db.SaveChangesAsync();

            if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
                requesterId = Guid.Empty;
            await WriteAuditAsync(record.EmployeeId, requesterId, record.Id, HttpContext.Connection.RemoteIpAddress?.ToString());
            await _dailySummaryService.GenerateForDateAsync(record.AttendanceDate, HttpContext.RequestAborted);
        }

        return Ok(Project(record));
    }

    // Re-queue a background face-match for every record that has a check-in photo — e.g. after the
    // references were corrected, to (re)score the history. Returns how many were queued.
    [HttpPost("recheck-faces")]
    public async Task<IActionResult> RecheckFaces()
    {
        var ids = await _db.AttendanceRecords
            .Where(r => r.CheckInPhotoKey != null)
            .Select(r => r.Id)
            .ToListAsync(HttpContext.RequestAborted);
        foreach (var id in ids)
            _faceQueue.Enqueue(id);
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
}
