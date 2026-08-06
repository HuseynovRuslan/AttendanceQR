using AttendanceQR.Domain.Entities;
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

    // GET /api/tenant/branding — the current tenant's display name + accent colour, plus the features
    // its plan has switched off so the app can hide what the server would 403 anyway.
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<IActionResult> Branding()
    {
        // Asked by someone whose subdomain names no tenant (the apex, a direct api.qrlog.az hit):
        // there is nothing to brand as, and guessing means showing one company's identity to another.
        if (!_tenant.IsResolved)
            return Ok(new { displayName = string.Empty, color = (string?)null, logoUrl = (string?)null, disabledFeatures = Array.Empty<string>() });

        // Tenants is not query-filtered (it's the tenant registry itself), so this reads the resolved
        // tenant directly.
        var b = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { t.DisplayName, t.Color, t.LogoKey, t.DisabledFeatures })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (b is null)
            return Ok(new { displayName = string.Empty, color = (string?)null, logoUrl = (string?)null, disabledFeatures = Array.Empty<string>() });

        return Ok(new
        {
            displayName = b.DisplayName,
            color = b.Color,
            logoUrl = b.LogoKey,
            disabledFeatures = TenantFeatures.ParseDisabled(b.DisabledFeatures).ToArray(),
        });
    }

    // GET /api/tenant/manifest — the PWA manifest, per tenant. The frontend nginx proxies
    // <slug>.qrlog.az/manifest.webmanifest here (same-origin), so an installed "Add to Home Screen"
    // gets the tenant's own name + logo instead of a shared one. Tenant resolved from the Host header.
    [HttpGet("manifest")]
    [AllowAnonymous]
    public async Task<IActionResult> Manifest()
    {
        // Unknown subdomain → the generic manifest below (no name, default icons), never another
        // tenant's. See Branding().
        var t = !_tenant.IsResolved ? null : await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { t.DisplayName, t.LogoKey })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        // No tenant (the shared app./admin. hosts) → the product brand "QRLog". A tenant → its own name
        // with the brand as the qualifier ("EastCaf — QRLog"); short_name (under the icon) stays the bare
        // company name. Either way the generic word "Davamiyyət" is never the installed app's name.
        var display = string.IsNullOrWhiteSpace(t?.DisplayName) ? "QRLog" : t!.DisplayName;
        var name = string.IsNullOrWhiteSpace(t?.DisplayName) ? "QRLog" : $"{t!.DisplayName} — QRLog";

        // A custom RASTER logo (a tenant uploaded their own PNG/JPG/WebP) becomes the install icon directly.
        // Otherwise — no logo, or the default QRLog .svg — fall back to the per-host PNG icon set at
        // /icon-*.png, which the frontend nginx serves per host (QRLog for a normal tenant, Bakı Abadlıq for
        // bax). SVG is deliberately NOT used as an install icon: iOS ignores manifest icons entirely (it reads
        // the apple-touch-icon link), and Chrome's launcher won't reliably rasterise an SVG — so a maskable
        // PNG is the only icon that installs correctly on both. This is why a new company was getting Bakı
        // Abadlıq's leaf: its default .svg logo produced no usable install icon, so the static shared PNGs won.
        var key = t?.LogoKey;
        var isRaster = !string.IsNullOrWhiteSpace(key) && (
            key!.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));

        List<Dictionary<string, object?>> icons;
        if (isRaster)
        {
            var mime = key!.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                     : key.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp"
                     : "image/jpeg";
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
    // Hosts we serve that are NOT tenants — the operator console (admin.qrlog.az) and the native-app
    // host (app.qrlog.az). They get an on-demand certificate through the catch-all just like a tenant
    // subdomain, so neither needs an explicit Caddy block (and adding admin needs no Caddy touch at all).
    private static readonly HashSet<string> ServedNonTenantHosts =
        new(StringComparer.OrdinalIgnoreCase) { "admin", "app" };

    [HttpGet("allow-tls")]
    [AllowAnonymous]
    public async Task<IActionResult> AllowTls([FromQuery] string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return NotFound();
        var slug = domain.Split('.')[0].ToLowerInvariant();
        if (ServedNonTenantHosts.Contains(slug))
            return Ok();
        var exists = await _db.Tenants.AnyAsync(t => t.Slug == slug && t.IsActive, HttpContext.RequestAborted);
        return exists ? Ok() : NotFound();
    }
}
