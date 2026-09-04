using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
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

    /// <summary>GET /api/push/inbox — the reminders sent to this employee, newest first. A push banner
    /// is gone the moment it's swiped away, so the app keeps its own copy for the notifications tab.</summary>
    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox()
    {
        var employeeId = User.EmployeeId();
        var rows = await _db.EmployeeNotifications
            .Where(n => n.EmployeeId == employeeId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(50)
            .Select(n => new
            {
                id = n.Id,
                type = n.Type.ToString(),
                title = n.Title,
                body = n.Body,
                createdAtUtc = n.CreatedAtUtc,
            })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    /// <summary>
    /// GET /api/push/pending-warning — the admin warning this employee has not acknowledged yet.
    ///
    /// Separate from the inbox above and read on every home load, because the inbox is a tab people
    /// visit and this is a message that has to find them. Aydın's record is the argument: five
    /// no-face photographs and a photograph of an actor over twelve scans, and nothing in the app
    /// ever stopped him — the passive banner beside it counts only NoFace, only within the calendar
    /// month, and had reset to zero two days before the worst one.
    ///
    /// Oldest first: if two were somehow sent, the earlier one is answered first.
    /// </summary>
    [HttpGet("pending-warning")]
    public async Task<IActionResult> PendingWarning()
    {
        var employeeId = User.EmployeeId();
        var row = await _db.EmployeeNotifications
            .Where(n => n.EmployeeId == employeeId
                        && n.Type == EmployeeNotificationType.PhotoWarning
                        && n.AcknowledgedAtUtc == null)
            .OrderBy(n => n.CreatedAtUtc)
            .Select(n => new { id = n.Id, title = n.Title, body = n.Body, createdAtUtc = n.CreatedAtUtc })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        return Ok(row is null ? null : (object)row);
    }

    /// <summary>POST /api/push/warning/{id}/ack — «Anladım». Scoped to the caller's own rows, so one
    /// employee cannot dismiss another's.</summary>
    [HttpPost("warning/{id:guid}/ack")]
    public async Task<IActionResult> AcknowledgeWarning(Guid id)
    {
        var employeeId = User.EmployeeId();
        var row = await _db.EmployeeNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.EmployeeId == employeeId,
                                 HttpContext.RequestAborted);
        if (row is null)
            return NotFound(new { error = "NotFound" });

        row.AcknowledgedAtUtc ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { acknowledged = true });
    }

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

    /// <summary>Registers a NATIVE app device (Capacitor Android/iOS) for FCM push. The app's WebView
    /// cannot do Web Push, so the native layer hands us an FCM token instead. Stored as a subscription
    /// with FcmToken set; Endpoint carries "fcm:{token}" to satisfy the unique Endpoint index.</summary>
    [HttpPost("register-native")]
    public async Task<IActionResult> RegisterNative([FromBody] PushRegisterNativeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "InvalidToken" });

        var employeeId = User.EmployeeId();
        var endpoint = "fcm:" + request.Token;

        // The token is globally unique. Re-registering (or a phone changing hands) re-points the row
        // rather than duplicating. IgnoreQueryFilters so a row from another tenant is adopted, not a
        // unique-index collision.
        var existing = await _db.PushSubscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Endpoint == endpoint, ct);

        if (existing is not null)
        {
            existing.EmployeeId = employeeId;
            existing.FcmToken = request.Token;
            existing.TenantId = _db.CurrentTenantId;
        }
        else
        {
            _db.PushSubscriptions.Add(new Domain.Entities.PushSubscription
            {
                EmployeeId = employeeId,
                FcmToken = request.Token,
                Endpoint = endpoint,
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    /// <summary>Forgets a native device's FCM token (the app was signed out / notifications disabled).</summary>
    [HttpPost("unregister-native")]
    public async Task<IActionResult> UnregisterNative([FromBody] PushRegisterNativeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var employeeId = User.EmployeeId();
        var rows = await _db.PushSubscriptions
            .Where(p => p.FcmToken == request.Token && p.EmployeeId == employeeId)
            .ToListAsync(ct);
        _db.PushSubscriptions.RemoveRange(rows);
        await _db.SaveChangesAsync(ct);
        return Ok(new { removed = rows.Count });
    }
}
