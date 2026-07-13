using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>Public tenant info the app needs to brand itself before login. The tenant is resolved
/// from the request Origin (subdomain) by the middleware, so no auth is required.</summary>
[ApiController]
[Route("api/tenant")]
public class TenantController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public TenantController(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // GET /api/tenant/branding — the current tenant's display name + accent colour.
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<IActionResult> Branding()
    {
        // Tenants is not query-filtered (it's the tenant registry itself), so this reads the resolved
        // tenant directly.
        var b = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { displayName = t.DisplayName, color = t.Color, logoUrl = t.LogoKey })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        return Ok(b ?? new { displayName = string.Empty, color = (string?)null, logoUrl = (string?)null });
    }

    // GET /api/tenant/manifest — the PWA manifest, per tenant. The frontend nginx proxies
    // <slug>.qrlog.az/manifest.webmanifest here (same-origin), so an installed "Add to Home Screen"
    // gets the tenant's own name + logo instead of a shared one. Tenant resolved from the Host header.
    [HttpGet("manifest")]
    [AllowAnonymous]
    public async Task<IActionResult> Manifest()
    {
        var t = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { t.DisplayName, t.LogoKey })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        var display = string.IsNullOrWhiteSpace(t?.DisplayName) ? "Davamiyyət" : t!.DisplayName;
        var name = string.IsNullOrWhiteSpace(t?.DisplayName) ? "Davamiyyət" : $"{t!.DisplayName} — Davamiyyət";

        List<Dictionary<string, object?>> icons;
        if (!string.IsNullOrWhiteSpace(t?.LogoKey))
        {
            var key = t!.LogoKey!;
            var mime = key.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                     : key.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                     : key.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
                     : "image/jpeg";
            // One square logo, declared at the sizes browsers look for on install (scaled from source).
            icons = new() { new() { ["src"] = key, ["sizes"] = "192x192 512x512", ["type"] = mime, ["purpose"] = "any" } };
        }
        else
        {
            icons = new()
            {
                new() { ["src"] = "/icon-192.png", ["sizes"] = "192x192", ["type"] = "image/png", ["purpose"] = "any maskable" },
                new() { ["src"] = "/icon-512.png", ["sizes"] = "512x512", ["type"] = "image/png", ["purpose"] = "any maskable" },
            };
        }

        var manifest = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["short_name"] = display,
            ["description"] = "QR ilə giriş-çıxış qeydiyyatı",
            ["lang"] = "az",
            ["start_url"] = "/",
            ["scope"] = "/",
            ["display"] = "standalone",
            ["orientation"] = "portrait",
            ["background_color"] = "#F7F6F2",
            ["theme_color"] = "#18191A",
            ["icons"] = icons,
        };

        return Content(System.Text.Json.JsonSerializer.Serialize(manifest), "application/manifest+json");
    }

    // GET /api/tenant/allow-tls?domain=<host> — Caddy's on-demand-TLS gate. Only issue a certificate
    // for a subdomain that maps to a real, active tenant, so nobody can trigger cert issuance for
    // random *.qrlog.az names. 200 = allow, 404 = deny.
    [HttpGet("allow-tls")]
    [AllowAnonymous]
    public async Task<IActionResult> AllowTls([FromQuery] string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return NotFound();
        var slug = domain.Split('.')[0].ToLowerInvariant();
        var exists = await _db.Tenants.AnyAsync(t => t.Slug == slug && t.IsActive, HttpContext.RequestAborted);
        return exists ? Ok() : NotFound();
    }
}
