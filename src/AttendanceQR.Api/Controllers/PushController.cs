using AttendanceQR.Api.Contracts;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Web Push subscription management for the signed-in employee. The browser subscribes with the VAPID
/// public key served here and posts the resulting endpoint/keys back; the checkout-reminder job then
/// has somewhere to send to. Tenant-scoped.
/// </summary>
[ApiController]
[Authorize]
[Route("api/push")]
public class PushController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PushOptions _options;
    private readonly IPushNotifier _notifier;

    public PushController(AppDbContext db, PushOptions options, IPushNotifier notifier)
    {
        _db = db;
        _options = options;
        _notifier = notifier;
    }

    /// <summary>Sends a test notification to the caller's OWN devices — the way to check the whole
    /// chain (subscription → server → push service → phone) without broadcasting to anyone else.</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test()
    {
        var reached = await _notifier.NotifyEmployeesAsync(
            new[] { User.EmployeeId() },
            "Test bildirişi",
            "Bildirişlər işləyir ✓ Elanlar və xatırlatmalar bu cür gələcək.",
            "/home",
            HttpContext.RequestAborted);
        return Ok(new { reached });
    }

    /// <summary>The VAPID public key the browser needs to subscribe. `enabled:false` = push is not
    /// configured on this server, and the client should not offer it.</summary>
    [HttpGet("public-key")]
    public IActionResult PublicKey()
        => Ok(new { enabled = _options.IsConfigured, publicKey = _options.PublicKey });

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.P256dh)
            || string.IsNullOrWhiteSpace(request.Auth))
            return BadRequest(new { error = "InvalidSubscription" });

        var employeeId = User.EmployeeId();

        // The endpoint is globally unique. Re-subscribing (or a phone changing hands) updates the row
        // rather than piling up duplicates. IgnoreQueryFilters so a row left behind by another tenant
        // is re-pointed instead of colliding with the unique index.
        var existing = await _db.PushSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Endpoint == request.Endpoint, ct);

        if (existing is not null)
        {
            existing.EmployeeId = employeeId;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
            existing.TenantId = _db.CurrentTenantId;
        }
        else
        {
            _db.PushSubscriptions.Add(new Domain.Entities.PushSubscription
            {
                EmployeeId = employeeId,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var employeeId = User.EmployeeId();
        var rows = await _db.PushSubscriptions
            .Where(p => p.Endpoint == request.Endpoint && p.EmployeeId == employeeId)
            .ToListAsync(ct);
        _db.PushSubscriptions.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        return Ok(new { removed = rows.Count });
    }
}
