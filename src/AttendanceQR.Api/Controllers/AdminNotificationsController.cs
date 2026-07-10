using System.Security.Claims;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin bell-icon notifications — computed live from existing data on every call, no persisted
/// Notification entity, no read/unread state. Two triggers for now: a pending device-change
/// request (one item each, individually actionable) and today's late count (one summarized item
/// linking to the live board). Always reflects current state, so there's nothing to "dismiss" —
/// approving a device change or the day rolling over naturally drops the count on the next poll.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/notifications")]
public class AdminNotificationsController : ControllerBase
{
    // Bounds the dropdown if pending requests ever pile up — the badge's totalCount still reflects
    // the true number, only the itemized list is capped.
    private const int MaxPendingItems = 20;

    private readonly IDeviceChangeService _deviceChangeService;
    private readonly IReportQueryService _reports;

    public AdminNotificationsController(IDeviceChangeService deviceChangeService, IReportQueryService reports)
    {
        _deviceChangeService = deviceChangeService;
        _reports = reports;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var requesterId))
            return Unauthorized(new { error = "InvalidToken" });

        var pending = await _deviceChangeService.GetPendingAsync(HttpContext.RequestAborted);

        // No "N employees were late today": every employee keeps their own hours, so a location-wide
        // shift cannot decide who was late — the alert was simply wrong, every morning.
        var items = pending
            .Take(MaxPendingItems)
            .Select(p => new
            {
                type = "PendingDeviceChange",
                message = $"{p.EmployeeName} — yeni cihaz təsdiqi gözləyir",
                linkTo = "/admin/device-changes"
            })
            .ToList<object>();

        return Ok(new
        {
            totalCount = pending.Count,
            items
        });
    }
}
