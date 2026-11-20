using System.Globalization;
using AttendanceQR.Api.DailyReport;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting.DailyReport;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <param name="Time">"HH:mm" (company local time) to send every day; null or empty = only on «send now».</param>
public sealed record DailyReportRecipientRequest(string Name, List<Guid>? LocationIds, string? Time = null);
public sealed record DailyReportSendNowRequest(Guid? RecipientId);

/// <summary>
/// The daily management report screen: the leadership recipients, which branches each one receives and at
/// what time, a one-time «connect» link per recipient, «send now», and a preview (HTML or PDF) that sends
/// nothing.
///
/// The company admin works on the whole company. A structure manager works on the branches they run, and
/// only there: they choose recipients among their own branches (never «the whole company»), see and touch
/// only the recipients whose branches all lie inside theirs, and their preview and «send now» cover nothing
/// else. The report is a view of the branches somebody runs, so it narrows exactly like every other board.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
// Reading (the screen, the preview) is «attendance.view». Everything that sends the figures somewhere or decides who
// gets them is «notifications.staff_alerts», which a view-only role (Müşahidəçi) does not hold — see each action.
[Permission(AccessPermissions.AttendanceView)]
[Route("api/admin/daily-report")]
public class AdminDailyReportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DailyReportService _service;
    private readonly DailyReportOptions _options;
    private readonly IPdfRenderer _pdf;

    public AdminDailyReportController(AppDbContext db, DailyReportService service, DailyReportOptions options, IPdfRenderer pdf)
    {
        _db = db;
        _service = service;
        _options = options;
        _pdf = pdf;
    }

    /// <summary>Null = the whole company (an admin); otherwise the branches this manager runs (possibly none).</summary>
    private async Task<HashSet<Guid>?> ManagedAsync(CancellationToken ct) =>
        User.Role() == EmployeeRole.Admin
            ? null
            : (await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), ct)).ToHashSet();

    /// <summary>
    /// May this caller see the recipient? An admin, any. A manager, only one with branches named, every one of
    /// them theirs — a whole-company recipient (no branches) is the admin's.
    /// </summary>
    private static bool Visible(DailyReportRecipient r, HashSet<Guid>? managed) =>
        managed is null || DailyReportService.ParseScope(r.LocationIds) is { } scope && scope.All(managed.Contains);

    private async Task<DailyReportRecipient?> FindVisibleAsync(Guid id, HashSet<Guid>? managed, CancellationToken ct)
    {
        var r = await _db.DailyReportRecipients.FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        return r is not null && Visible(r, managed) ? r : null;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedAsync(ct);
        // Inactive ones too, so a removed recipient's past deliveries still carry its name.
        var all = (await _db.DailyReportRecipients.AsNoTracking().OrderBy(r => r.CreatedAtUtc).ToListAsync(ct))
            .Where(r => Visible(r, managed))
            .ToList();
        var visibleIds = all.Select(r => r.Id).ToHashSet();
        var names = all.ToDictionary(r => r.Id, r => r.Name);
        var deliveries = (await _db.DailyReportDeliveries.AsNoTracking()
                .OrderByDescending(d => d.CreatedAtUtc)
                .Take(300)
                .Select(d => new { d.Id, d.RecipientId, d.ReportDate, d.Kind, d.Status, d.Attempts, d.LastError, d.CreatedAtUtc, d.SentAtUtc })
                .ToListAsync(ct))
            .Where(d => visibleIds.Contains(d.RecipientId))
            .Take(30);
        return Ok(new
        {
            botConfigured = _options.TelegramConfigured,
            botUsername = _options.TelegramBotUsername.TrimStart('@'),
            // Only the company admin may address a recipient to the whole company.
            canWholeCompany = managed is null,
            recipients = all.Where(r => r.IsActive).Select(r => new
            {
                r.Id,
                r.Name,
                locationIds = DailyReportService.ParseScope(r.LocationIds)?.ToList() ?? [],
                time = r.SendAtLocal?.ToString("HH:mm", CultureInfo.InvariantCulture),
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

    [Permission(AccessPermissions.NotificationsStaffAlerts)]
    [HttpPost("recipients")]
    public async Task<IActionResult> CreateRecipient([FromBody] DailyReportRecipientRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var (name, scope, time, error) = await ValidateAsync(request, await ManagedAsync(ct), ct);
        if (error is not null) return BadRequest(new { error });
        var r = new DailyReportRecipient
        {
            Name = name!, LocationIds = scope, SendAtLocal = time, CreatedByEmployeeId = User.EmployeeId(),
        };
        _db.DailyReportRecipients.Add(r);
        var link = _options.TelegramConfigured ? _service.NewConnectLink(r) : null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { r.Id, connectLink = link });
    }

    [Permission(AccessPermissions.NotificationsStaffAlerts)]
    [HttpPut("recipients/{id:guid}")]
    public async Task<IActionResult> UpdateRecipient(Guid id, [FromBody] DailyReportRecipientRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedAsync(ct);
        var r = await FindVisibleAsync(id, managed, ct);
        if (r is null) return NotFound(new { error = "RecipientNotFound" });
        var (name, scope, time, error) = await ValidateAsync(request, managed, ct);
        if (error is not null) return BadRequest(new { error });
        r.Name = name!;
        r.LocationIds = scope;
        r.SendAtLocal = time;
        await _db.SaveChangesAsync(ct);
        return Ok(new { r.Id });
    }

    /// <summary>Stops sending to this person and disconnects their chat. Their old links stop opening too.</summary>
    [Permission(AccessPermissions.NotificationsStaffAlerts)]
    [HttpDelete("recipients/{id:guid}")]
    public async Task<IActionResult> RemoveRecipient(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var r = await FindVisibleAsync(id, await ManagedAsync(ct), ct);
        if (r is null) return NotFound(new { error = "RecipientNotFound" });
        r.IsActive = false;
        r.TelegramChatId = null;
        r.LinkCodeHash = null;
        r.LinkCodeExpiresUtc = null;
        await _db.SaveChangesAsync(ct);
        return Ok(new { removed = id });
    }

    /// <summary>A new one-time «connect» link (48 h). Also how a recipient moves to a new phone: the new chat replaces the old.</summary>
    [Permission(AccessPermissions.NotificationsStaffAlerts)]
    [HttpPost("recipients/{id:guid}/connect-link")]
    public async Task<IActionResult> ConnectLink(Guid id)
    {
        if (!_options.TelegramConfigured) return BadRequest(new { error = "BotNotConfigured" });
        var ct = HttpContext.RequestAborted;
        var r = await FindVisibleAsync(id, await ManagedAsync(ct), ct);
        if (r is null) return NotFound(new { error = "RecipientNotFound" });
        var link = _service.NewConnectLink(r);
        await _db.SaveChangesAsync(ct);
        return Ok(new { connectLink = link });
    }

    [Permission(AccessPermissions.NotificationsStaffAlerts)]
    [HttpPost("send-now")]
    public async Task<IActionResult> SendNow([FromBody] DailyReportSendNowRequest? request)
    {
        if (!_options.TelegramConfigured) return BadRequest(new { error = "BotNotConfigured" });
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedAsync(ct);
        IReadOnlySet<Guid>? allowed = null;
        if (managed is not null)
            allowed = (await _db.DailyReportRecipients.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct))
                .Where(r => Visible(r, managed)).Select(r => r.Id).ToHashSet();
        var outcomes = await _service.SendNowAsync(request?.RecipientId, allowed, ct);
        if (outcomes.Count == 0) return BadRequest(new { error = "NoConnectedRecipients" });
        return Ok(new { results = outcomes.Select(o => new { o.RecipientName, o.Sent, o.Error }) });
    }

    /// <summary>
    /// Today's report as it would be sent now — nothing is sent, nothing is stored. For one recipient, their
    /// branches; otherwise the caller's own: the whole company for an admin, a manager's branches for a manager.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] Guid? recipientId, [FromQuery] string? format)
    {
        var ct = HttpContext.RequestAborted;
        var managed = await ManagedAsync(ct);
        string? scope;
        if (recipientId is Guid rid)
        {
            var r = await FindVisibleAsync(rid, managed, ct);
            if (r is null) return NotFound(new { error = "RecipientNotFound" });
            scope = r.LocationIds;
        }
        else if (managed is null)
        {
            scope = null;
        }
        else
        {
            // An empty scope means «the whole company» to the builder — a manager who runs nothing gets nothing.
            if (managed.Count == 0) return BadRequest(new { error = "NoBranches" });
            scope = string.Join(',', managed);
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

    private async Task<(string? Name, string? Scope, TimeOnly? Time, string? Error)> ValidateAsync(
        DailyReportRecipientRequest request, HashSet<Guid>? managed, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, null, null, "NameRequired");
        if (name.Length > 120) return (null, null, null, "NameTooLong");

        TimeOnly? time = null;
        if (!string.IsNullOrWhiteSpace(request.Time))
        {
            if (!TimeOnly.TryParseExact(request.Time.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return (null, null, null, "TimeInvalid");
            time = parsed;
        }

        var ids = (request.LocationIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            // «The whole company» — the company admin's call only.
            return managed is null ? (name, null, time, null) : (null, null, null, "LocationsRequired");
        if (managed is not null && !ids.All(managed.Contains)) return (null, null, null, "LocationOutOfScope");
        // Only this company's branches — the filter guarantees it; a foreign id simply does not match.
        var known = await _db.Locations.Where(l => ids.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
        if (known.Count != ids.Count) return (null, null, null, "LocationNotFound");
        return (name, string.Join(',', known), time, null);
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
