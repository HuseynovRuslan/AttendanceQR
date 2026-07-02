using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Public kiosk endpoints. The token action returns a freshly <b>signed</b> QR token (never the
/// signing secret), so anonymous access is safe: the token only identifies a location and expires
/// with TtlSeconds; every scan still requires an employee JWT, a bound device, and the geofence.
/// The location action lets the kiosk screen show which location it belongs to.
/// </summary>
[ApiController]
[Route("api/kiosk")]
public class KioskController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IQrTokenService _qrTokenService;
    private readonly QrTokenOptions _qrTokenOptions;

    public KioskController(AppDbContext db, IQrTokenService qrTokenService, IOptions<QrTokenOptions> qrTokenOptions)
    {
        _db = db;
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

    // Name (and existence) of a location, so the kiosk header can show it. Anonymous — the name is
    // not sensitive. 404 lets a misconfigured kiosk URL surface an error instead of a blank screen.
    [HttpGet("location/{locationId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Location(Guid locationId)
    {
        var location = await _db.Locations
            .Where(l => l.Id == locationId)
            .Select(l => new { l.Id, l.Name })
            .FirstOrDefaultAsync();

        if (location is null)
            return NotFound(new { error = "LocationNotFound" });

        return Ok(location);
    }
}
