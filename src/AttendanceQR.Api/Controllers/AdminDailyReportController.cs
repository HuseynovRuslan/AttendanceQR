using System.Globalization;
using AttendanceQR.Api.DailyReport;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting.DailyReport;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

public sealed record DailyReportSettingsRequest(string? Time);
public sealed record DailyReportRecipientRequest(string Name, List<Guid>? LocationIds);
public sealed record DailyReportSendNowRequest(Guid? RecipientId);

/// <summary>
/// The company admin's screen for the daily management report: the send time (off by default), the
/// leadership recipients and which branches each one receives, a one-time «connect» link per recipient,
/// «send now», and a preview (HTML or PDF) that sends nothing. Company admins only — the report shows
/// the whole company's figures, and choosing who receives them is a company decision.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
// The company's attendance figures, company-wide. Admin-only and not Scoped, so in Enforced mode it stays the
// Company administrator's (PermissionGateHandler) whatever else a custom role holds.
[Permission(AccessPermissions.AttendanceView, AtCompany = true)]
[Route("api/admin/daily-report")]
public class AdminDailyReportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DailyReportService _service;
    private readonly DailyReportOptions _options;
    private readonly IPdfRenderer _pdf;
    private readonly ITenantContext _tenant;

    public AdminDailyReportController(AppDbContext db, DailyReportService service, DailyReportOptions options,
        IPdfRenderer pdf, ITenantContext tenant)
    {
        _db = db;
        _service = service;
        _options = options;
        _pdf = pdf;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _tenant.TenantId, ct);
        var recipients = await _db.DailyReportRecipients.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);
        var names = recipients.ToDictionary(r => r.Id, r => r.Name);
        var deliveries = await _db.DailyReportDeliveries.AsNoTracking()
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(30)
            .Select(d => new { d.Id, d.RecipientId, d.ReportDate, d.Kind, d.Status, d.Attempts, d.LastError, d.CreatedAtUtc, d.SentAtUtc })
            .ToListAsync(ct);
        return Ok(new
        {
            time = tenant.DailyReportAtLocal?.ToString("HH:mm", CultureInfo.InvariantCulture),
            botConfigured = _options.TelegramConfigured,
            botUsername = _options.TelegramBotUsername.TrimStart('@'),
            recipients = recipients.Select(r => new
            {
                r.Id,
                r.Name,
                locationIds = DailyReportService.ParseScope(r.LocationIds)?.ToList() ?? [],
                linked = r.TelegramChatId != null,
                r.LinkedAtUtc,
                linkPending = r.LinkCodeExpiresUtc != null && r.LinkCodeExpiresUtc > DateTime.UtcNow,
            }),
            deliveries = deliveries.Select(d => new
            {
                d.Id,
                recipientName = names.GetValueOrDefault(d.RecipientId) ?? "—",
                date = d.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                kind = d.Kind == (int)DeliveryKind.Manual ? "manual" : "scheduled",
                status = d.Status switch { (int)DeliveryStatus.Sent => "sent", (int)DeliveryStatus.Failed => "failed", _ => "pending" },
                d.Attempts,
                d.LastError,
                d.CreatedAtUtc,
                d.SentAtUtc,
            }),
        });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> PutSettings([FromBody] DailyReportSettingsRequest request)
    {
        TimeOnly? at = null;
        if (!string.IsNullOrWhiteSpace(request.Time))
        {
            if (!TimeOnly.TryParseExact(request.Time.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return BadRequest(new { error = "TimeInvalid" });
            at = parsed;
        }
        var tenant = await _db.Tenants.FirstAsync(t => t.Id == _tenant.TenantId, HttpContext.RequestAborted);
        tenant.DailyReportAtLocal = at;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { time = at?.ToString("HH:mm", CultureInfo.InvariantCulture) });
    }

    [HttpPost("recipients")]
    public async Task<IActionResult> CreateRecipient([FromBody] DailyReportRecipientRequest request)
    {
        var (name, scope, error) = await ValidateAsync(request);
        if (error is not null) return BadRequest(new { error });
        var r = new DailyReportRecipient { Name = name!, LocationIds = scope, CreatedByEmployeeId = User.EmployeeId() };
        _db.DailyReportRecipients.Add(r);
        var link = _options.TelegramConfigured ? _service.NewConnectLink(r) : null;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { r.Id, connectLink = link });
    }

    [HttpPut("recipients/{id:guid}")]
    public async Task<IActionResult> UpdateRecipient(Guid id, [FromBody] DailyReportRecipientRequest request)
    {
        var r = await _db.DailyReportRecipients.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, HttpContext.RequestAborted);
        if (r is null) return NotFound(new { error = "RecipientNotFound" });
        var (name, scope, error) = await ValidateAsync(request);
        if (error is not null) return BadRequest(new { error });
        r.Name = name!;
        r.LocationIds = scope;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { r.Id });
    }

    /// <summary>Stops sending to this person and disconnects their chat. Their old links stop opening too.</summary>
    [HttpDelete("recipients/{id:guid}")]
    public async Task<IActionResult> RemoveRecipient(Guid id)
    {
        var r = await _db.DailyReportRecipients.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, HttpContext.RequestAborted);
        if (r is null) return NotFound(new { error = "RecipientNotFound" });
        r.IsActive = false;
        r.TelegramChatId = null;
        r.LinkCodeHash = null;
        r.LinkCodeExpiresUtc = null;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { removed = id });
    }

    /// <summary>A new one-time «connect» link (48 h). Also how a recipient moves to a new phone: the new chat replaces the old.</summary>
    [HttpPost("recipients/{id:guid}/connect-link")]
    public async Task<IActionResult> ConnectLink(Guid id)
    {
        if (!_options.TelegramConfigured) return BadRequest(new { error = "BotNotConfigured" });
        var r = await _db.DailyReportRecipients.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, HttpContext.RequestAborted);
        if (r is null) return NotFound(new { error = "RecipientNotFound" });
        var link = _service.NewConnectLink(r);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { connectLink = link });
    }

    [HttpPost("send-now")]
    public async Task<IActionResult> SendNow([FromBody] DailyReportSendNowRequest? request)
    {
        if (!_options.TelegramConfigured) return BadRequest(new { error = "BotNotConfigured" });
        var outcomes = await _service.SendNowAsync(request?.RecipientId, HttpContext.RequestAborted);
        if (outcomes.Count == 0) return BadRequest(new { error = "NoConnectedRecipients" });
        return Ok(new { results = outcomes.Select(o => new { o.RecipientName, o.Sent, o.Error }) });
    }

    /// <summary>Today's report as it would be sent now — nothing is sent, nothing is stored.</summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] Guid? recipientId, [FromQuery] string? format)
    {
        var ct = HttpContext.RequestAborted;
        string? scope = null;
        if (recipientId is Guid rid)
        {
            var r = await _db.DailyReportRecipients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rid && x.IsActive, ct);
            if (r is null) return NotFound(new { error = "RecipientNotFound" });
            scope = r.LocationIds;
        }
        var data = await _service.BuildDataAsync(scope, ct);
        var html = DailyReportHtml.Render(data);
        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdf = await _pdf.RenderAsync(html, ct);
            return File(pdf, "application/pdf", $"davamiyyet-{data.Date:yyyy-MM-dd}.pdf");
        }
        return Content(html, "text/html; charset=utf-8");
    }

    private async Task<(string? Name, string? Scope, string? Error)> ValidateAsync(DailyReportRecipientRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, null, "NameRequired");
        if (name.Length > 120) return (null, null, "NameTooLong");
        var ids = (request.LocationIds ?? []).Distinct().ToList();
        if (ids.Count == 0) return (name, null, null);
        // Only this company's branches — the filter guarantees it; a foreign id simply does not match.
        var known = await _db.Locations.Where(l => ids.Contains(l.Id)).Select(l => l.Id).ToListAsync(HttpContext.RequestAborted);
        if (known.Count != ids.Count) return (null, null, "LocationNotFound");
        return (name, string.Join(',', known), null);
    }
}

/// <summary>
/// The page behind the «HTML hesabatı aç» button. No login — Telegram's in-app browser carries none — so
/// the link itself is the key: a random secret (only its hash stored), valid for a limited time, and dead
/// as soon as the recipient is removed. The page holds counts only, no names.
/// It is served on the API host (the company subdomains only serve the frontend), which names no company,
/// so the token does: the company is resolved from it — only an active one — BEFORE anything is looked up,
/// and from then on the ordinary tenant filter decides what the delivery id may match. Same approach as the
/// bot's «/start» link; no query filter is bypassed. The path is on the tenant middleware's optional list.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/daily-report")]
public class PublicDailyReportController : ControllerBase
{
    private readonly DailyReportService _service;
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public PublicDailyReportController(DailyReportService service, AppDbContext db, ITenantContext tenant)
    {
        _service = service;
        _db = db;
        _tenant = tenant;
    }

    [HttpGet("{token}")]
    public async Task<IActionResult> Open(string token)
    {
        var h = Response.Headers;
        h.CacheControl = "no-store";
        h["X-Robots-Tag"] = "noindex, nofollow";
        h["Referrer-Policy"] = "no-referrer";
        h["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

        if (token.Length > 200 || !DailyReportService.TryParseToken(token, out var tenantId, out _, out _))
            return NotFoundPage();
        if (_tenant.IsResolved)
        {
            // Opened through some company's host: it must be the company the link belongs to.
            if (_tenant.TenantId != tenantId) return NotFoundPage();
        }
        else
        {
            var active = await _db.Tenants.AnyAsync(t => t.Id == tenantId && t.IsActive, HttpContext.RequestAborted);
            if (!active) return NotFoundPage();
            _tenant.Resolve(tenantId);
        }
        var (result, html) = await _service.OpenAsync(token, HttpContext.RequestAborted);
        return result switch
        {
            DailyReportService.OpenResult.Ok => Content(html!, "text/html; charset=utf-8"),
            DailyReportService.OpenResult.Expired => Message(StatusCodes.Status410Gone, "Linkin müddəti bitib",
                "Bu hesabatın linki artıq etibarlı deyil. Ən son hesabat Telegram-dakı yeni mesajdadır."),
            _ => NotFoundPage(),
        };
    }

    private ContentResult NotFoundPage() => Message(StatusCodes.Status404NotFound, "Hesabat tapılmadı",
        "Link səhvdir və ya artıq etibarlı deyil.");

    private static ContentResult Message(int status, string title, string text) => new()
    {
        StatusCode = status,
        ContentType = "text/html; charset=utf-8",
        Content = "<!doctype html><html lang=\"az\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                  "<meta name=\"robots\" content=\"noindex\"><title>QRLog</title><style>body{font-family:system-ui,-apple-system,'Segoe UI',Roboto,'Noto Sans',Arial,sans-serif;" +
                  "background:#F6F7F9;color:#1F2937;display:flex;min-height:100vh;align-items:center;justify-content:center;margin:0;padding:24px}" +
                  "div{background:#fff;border-radius:13px;padding:24px 22px;max-width:380px;box-shadow:0 1px 2px rgba(17,24,39,.05)}h1{font-size:18px;margin:0 0 8px}p{font-size:14px;color:#6B7280;margin:0}</style></head>" +
                  $"<body><div><h1>{title}</h1><p>{text}</p></div></body></html>",
    };
}
