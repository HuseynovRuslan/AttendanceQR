using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin side of the device-change flow: review the queue, approve or reject.
///
/// A branch manager reviews their own staff's requests here. It is the request they are best placed
/// to judge — they know whether that person actually changed phone — and the alternative was an
/// employee unable to scan until an admin looked at a queue in another building.
///
/// Scope is not applied in the service, which has no notion of a caller: the queue is filtered here
/// and each action re-checks its own request, both through CanManageEmployeeAsync (Role==Employee
/// inside their ManagedLocations).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/device-change")]
public class AdminDeviceChangeController : ControllerBase
{
    private readonly IDeviceChangeService _deviceChangeService;
    private readonly AppDbContext _db;

    public AdminDeviceChangeController(IDeviceChangeService deviceChangeService, AppDbContext db)
    {
        _deviceChangeService = deviceChangeService;
        _db = db;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var ct = HttpContext.RequestAborted;
        var rows = await _deviceChangeService.GetPendingAsync(ct);

        if (User.Role() == EmployeeRole.Manager)
        {
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), ct);
            var mine = await _db.Employees
                .Where(e => managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee)
                .Select(e => e.Id)
                .ToListAsync(ct);
            rows = rows.Where(r => mine.Contains(r.EmployeeId)).ToList();
        }

        return Ok(rows);
    }

    /// <summary>The request's subject, or a refusal when this caller may not act on them.</summary>
    private async Task<IActionResult?> OutOfScopeAsync(Guid requestId, CancellationToken ct)
    {
        var owner = await _db.DeviceChangeRequests
            .Where(r => r.Id == requestId)
            .Select(r => (Guid?)r.EmployeeId)
            .FirstOrDefaultAsync(ct);
        if (owner is null)
            return NotFound(new { error = "RequestNotFound" });
        return await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), owner.Value, ct)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (await OutOfScopeAsync(id, HttpContext.RequestAborted) is { } refusal)
            return refusal;

        var adminId = User.EmployeeId();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _deviceChangeService.ApproveAsync(id, adminId, ip, HttpContext.RequestAborted);

        return outcome switch
        {
            ReviewDeviceChangeOutcome.NotFound => NotFound(new { error = "RequestNotFound" }),
            ReviewDeviceChangeOutcome.AlreadyReviewed => Conflict(new { error = "AlreadyReviewed" }),
            _ => Ok(new { status = "Approved" })
        };
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id)
    {
        if (await OutOfScopeAsync(id, HttpContext.RequestAborted) is { } refusal)
            return refusal;

        var adminId = User.EmployeeId();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var outcome = await _deviceChangeService.RejectAsync(id, adminId, ip, HttpContext.RequestAborted);

        return outcome switch
        {
            ReviewDeviceChangeOutcome.NotFound => NotFound(new { error = "RequestNotFound" }),
            ReviewDeviceChangeOutcome.AlreadyReviewed => Conflict(new { error = "AlreadyReviewed" }),
            _ => Ok(new { status = "Rejected" })
        };
    }
}
