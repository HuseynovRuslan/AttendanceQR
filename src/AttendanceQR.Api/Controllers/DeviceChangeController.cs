using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>Employee side of the device-change flow. Any authenticated employee may request.</summary>
[ApiController]
[Authorize]
[Route("api/device-change")]
public class DeviceChangeController : ControllerBase
{
    private readonly IDeviceChangeService _deviceChangeService;

    public DeviceChangeController(IDeviceChangeService deviceChangeService)
    {
        _deviceChangeService = deviceChangeService;
    }

    [HttpPost("request")]
    public async Task<IActionResult> Submit([FromBody] DeviceChangeRequestBody body)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _deviceChangeService.RequestAsync(
            employeeId, body.NewDeviceFingerprint, ip, HttpContext.RequestAborted);

        if (result.Outcome == RequestDeviceChangeOutcome.PendingExists)
            return Conflict(new { error = "PendingRequestExists" });

        return StatusCode(
            StatusCodes.Status201Created,
            new { requestId = result.RequestId, status = "Pending" });
    }
}
