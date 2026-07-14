using System.Security.Claims;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Employee-supplied reason for arriving late or leaving early. The app prompts for it (skippably)
/// right after the scan when the check-in was late or the check-out early, and stores it on the
/// AttendanceRecord for the admin to see. Not an approval flow — just a note the employee attaches.
/// </summary>
[ApiController]
[Authorize]
[Route("api/attendance/reason")]
public class AttendanceReasonController : ControllerBase
{
    private readonly AppDbContext _db;

    public AttendanceReasonController(AppDbContext db) => _db = db;

    public record AttendanceReasonBody(Guid RecordId, string Kind, string Reason);

    // POST /api/attendance/reason — attach a late-arrival ("late") or early-departure ("early") reason
    // to one of the caller's own attendance records.
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] AttendanceReasonBody body)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        var kind = (body.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind != "late" && kind != "early")
            return BadRequest(new { error = "InvalidKind" });

        var reason = (body.Reason ?? string.Empty).Trim();
        if (reason.Length == 0)
            return BadRequest(new { error = "ReasonRequired" });
        if (reason.Length > 300)
            reason = reason[..300];

        // The global tenant query filter + EmployeeId match keep this to the caller's own record.
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(
            r => r.Id == body.RecordId && r.EmployeeId == employeeId, HttpContext.RequestAborted);
        if (record is null)
            return NotFound(new { error = "RecordNotFound" });

        if (kind == "late")
            record.LateArrivalReason = reason;
        else
            record.EarlyDepartureReason = reason;

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { ok = true });
    }
}
