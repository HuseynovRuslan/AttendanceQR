using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Public kiosk endpoint. Returns a freshly <b>signed</b> QR token (never the signing secret), so
/// anonymous access is safe: the token only identifies a location and expires with TtlSeconds; every
/// scan still requires an employee JWT, a bound device, and passing the geofence.
/// </summary>
[ApiController]
[Route("api/kiosk")]
public class KioskController : ControllerBase
{
    private readonly IQrTokenService _qrTokenService;
    private readonly QrTokenOptions _qrTokenOptions;

    public KioskController(IQrTokenService qrTokenService, IOptions<QrTokenOptions> qrTokenOptions)
    {
        _qrTokenService = qrTokenService;
        _qrTokenOptions = qrTokenOptions.Value;
    }

    [HttpGet("token/{locationId:guid}")]
    [AllowAnonymous]
    public IActionResult Token(Guid locationId)
    {
        var token = _qrTokenService.Generate(locationId);
        // Kiosk should refresh a few seconds before the token actually expires.
        var refreshInSeconds = Math.Max(1, _qrTokenOptions.TtlSeconds - 5);
        return Ok(new { token, locationId, refreshInSeconds });
    }
}
