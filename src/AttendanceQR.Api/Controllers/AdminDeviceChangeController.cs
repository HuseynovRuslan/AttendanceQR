using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>Admin side of the device-change flow: review the queue, approve or reject.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/device-change")]
public class AdminDeviceChangeController : ControllerBase
{
    private readonly IDeviceChangeService _deviceChangeService;

    public AdminDeviceChangeController(IDeviceChangeService deviceChangeService)
    {
        _deviceChangeService = deviceChangeService;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
        => Ok(await _deviceChangeService.GetPendingAsync(HttpContext.RequestAborted));

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id)
    {
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
