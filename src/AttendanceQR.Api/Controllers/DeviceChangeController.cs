using AttendanceQR.Api.Contracts;
using AttendanceQR.Infrastructure.Multitenancy;
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
    // The branch's managers may approve a request and used to have no way of knowing one had arrived —
    // a request on 02.10.2026 waited more than twenty hours. Optional, as everywhere a push is.
    private readonly IStaffAlertQueue? _alerts;
    private readonly ITenantContext? _tenant;
    private readonly TimeProvider _clock;

    public DeviceChangeController(
        IDeviceChangeService deviceChangeService, IStaffAlertQueue? alerts = null, ITenantContext? tenant = null,
        TimeProvider? clock = null)
    {
        _deviceChangeService = deviceChangeService;
        _alerts = alerts;
        _tenant = tenant;
        _clock = clock ?? TimeProvider.System;
    }

    [HttpPost("request")]
    public async Task<IActionResult> Submit([FromBody] DeviceChangeRequestBody body)
    {
        var employeeId = User.EmployeeId();

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _deviceChangeService.RequestAsync(
            employeeId, body.NewDeviceFingerprint, ip, HttpContext.RequestAborted);

        if (result.Outcome == RequestDeviceChangeOutcome.PendingExists)
            return Conflict(new { error = "PendingRequestExists" });

        if (_tenant is { IsResolved: true })
            _alerts?.Enqueue(new StaffAlert(_tenant.TenantId, employeeId, StaffAlertKinds.DeviceChangeRequested, _clock.GetUtcNow().UtcDateTime));

        return StatusCode(
            StatusCodes.Status201Created,
            new { requestId = result.RequestId, status = "Pending" });
    }
}
