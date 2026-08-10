using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Managing the companies themselves — the one place that looks ACROSS tenants rather than inside
/// one. Onboarding used to mean SSH: append TenantSeed__* to .env, redeploy, read a log line to
/// confirm, delete the vars again, then hand-edit the Caddyfile and restart it. This replaces all of
/// that; certificates now arrive on their own (see the catch-all in the Caddyfile).
///
/// Access is a config allowlist of employee IDs, not a role: a role lives inside a tenant, and a
/// tenant's own Admin must never be able to reach other tenants by editing their own row.
/// </summary>
[ApiController]
[Authorize]
[Route("api/super")]
public partial class SuperAdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwt;
    private readonly Guid[] _superAdminIds;
    private readonly AppOptions _appOptions;

    public SuperAdminController(AppDbContext db, ITenantContext tenant, IPasswordHasher passwordHasher, IJwtService jwt, AppOptions options)
    {
        _db = db;
        _tenant = tenant;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _superAdminIds = options.SuperAdminIdList();
        _appOptions = options;
    }

    // An impersonation token carries sub = the impersonated TENANT admin, not the operator. If that admin
    // is itself an operator, its sub would pass the allowlist and hand the impersonating session full
    // console powers — a Support operator could impersonate a Full operator's tenant and inherit Full.
    // An impersonation session is therefore never an operator session: it may act inside the target
    // tenant (the tenant routes), never against /api/super. This one check breaks the whole escalation
    // chain, since every read gate and CanAsync build on it.
    private bool IsSuperAdmin => !User.IsImpersonating() && _superAdminIds.Contains(User.EmployeeId());

    // The current operator's role. An allowlisted operator with NO profile row is Full — so introducing
    // roles takes no power away from anyone who has access today.
    private async Task<OperatorRoleType> CurrentRoleAsync(CancellationToken ct)
    {
        var id = User.EmployeeId();
        var role = await _db.OperatorProfiles
            .Where(p => p.EmployeeId == id)
            .Select(p => (OperatorRoleType?)p.Role)
            .FirstOrDefaultAsync(ct);
        return role ?? OperatorRoleType.Full;
    }

    // True only when the caller is an operator AND their role grants this power. Reads stay open to any
    // operator; every MUTATION gates on one of these.
    private async Task<bool> CanAsync(OperatorPermission perm, CancellationToken ct)
        => IsSuperAdmin && OperatorAccess.Allows(await CurrentRoleAsync(ct), perm);

    /// <summary>Lowercase letters, digits and dashes; 2–20 chars. It becomes a hostname.</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,19}$")]
    private static partial Regex SlugFormat();

    // GET /api/super/me — does this account manage tenants? The panel asks before showing the menu
    // item, so it never offers a screen that would only 403.
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (!IsSuperAdmin)
            return Ok(new { isSuperAdmin = false, role = (string?)null, permissions = Array.Empty<string>() });
        var role = await CurrentRoleAsync(HttpContext.RequestAborted);
        return Ok(new
        {
            isSuperAdmin = true,
            role = role.ToString(),
            permissions = OperatorAccess.PermissionsFor(role).Select(p => p.ToString()).ToArray(),
        });
    }

    // GET /api/super/tenants — every company, with the numbers that say whether it is really in use.
    [HttpGet("tenants")]
    public async Task<IActionResult> Tenants()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;

        // IgnoreQueryFilters: this is the one place that is meant to see across tenants. Tenants
        // itself carries no filter (it is the registry), but everything counted below does.
        var employeeCounts = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive)
            .GroupBy(e => e.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var locationCounts = await _db.Locations.IgnoreQueryFilters()
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        // Last scan tells you whether a company actually uses this, which "created 3 weeks ago" does not.
        var lastScan = await _db.AttendanceRecords.IgnoreQueryFilters()
            .GroupBy(r => r.TenantId)
            .Select(g => new { TenantId = g.Key, Last = g.Max(x => x.AttendanceDate) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Last, ct);

        var tenants = await _db.Tenants.OrderBy(t => t.CreatedAtUtc).ToListAsync(ct);

        return Ok(tenants.Select(t => new
        {
            id = t.Id,
            slug = t.Slug,
            displayName = t.DisplayName,
            color = t.Color,
            logoUrl = t.LogoKey,
            isActive = t.IsActive,
            createdAtUtc = t.CreatedAtUtc,
            host = $"{t.Slug}.qrlog.az",
            employeeCount = employeeCounts.GetValueOrDefault(t.Id, 0),
            locationCount = locationCounts.GetValueOrDefault(t.Id, 0),
            lastScanDate = lastScan.TryGetValue(t.Id, out var d) ? d.ToString("yyyy-MM-dd") : null,
            plan = t.Plan,
            maxEmployees = t.MaxEmployees,
            maxLocations = t.MaxLocations,
            monthlyPriceOverride = t.MonthlyPriceOverride,
            disabledFeatures = TenantFeatures.ParseDisabled(t.DisabledFeatures),
        }));
    }

    // POST /api/super/tenants — stand up a company: the tenant, a starter branch, and its first
    // admin, who signs in with their phone and a temporary PIN and must set their own on first login.
    // Mirrors the TenantSeed startup block it replaces, minus the redeploy.
    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
    {
        if (!await CanAsync(OperatorPermission.ManageTenants, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var slug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!SlugFormat().IsMatch(slug))
            return BadRequest(new { error = "SlugInvalid" });
        // The slug becomes <slug>.qrlog.az, so it must not collide with a name that is not a tenant —
        // TenantSlug.FromRequest refuses to resolve these, and a tenant living on one would be
        // unreachable in a way that looks like nothing at all.
        if (TenantSlug.IsReservedLabel(slug))
            return BadRequest(new { error = "SlugReserved" });
        if (await _db.Tenants.AnyAsync(t => t.Slug == slug, HttpContext.RequestAborted))
            return Conflict(new { error = "SlugTaken" });

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? slug : request.DisplayName.Trim();
        var phone = PhoneNumbers.Normalize(request.AdminPhone);
        if (phone is null)
            return BadRequest(new { error = "AdminPhoneInvalid" });

        var pin = string.IsNullOrWhiteSpace(request.AdminPin)
            ? PinRules.Generate()
            : request.AdminPin.Trim();
        if (!PinRules.IsWellFormed(pin))
            return BadRequest(new { error = "AdminPinInvalid" });
        if (PinRules.IsTooWeak(pin))
            return BadRequest(new { error = "AdminPinTooWeak" });

        var tenant = new Tenant
        {
            Name = displayName,
            Slug = slug,
            DisplayName = displayName,
            // Every tenant but the original wears QRLog's own identity — see the branding policy.
            Color = request.Color ?? "#1E70C8",
            LogoKey = request.LogoUrl ?? "/brand/qrlog.svg",
            IsActive = true,
        };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        // Everything from here belongs to the NEW company: the auto-stamp reads the request's tenant,
        // and this request's is the operator's. Same move the startup seed makes.
        _tenant.Resolve(tenant.Id);

        var location = new Location
        {
            Name = string.IsNullOrWhiteSpace(request.LocationName) ? "Baş ofis" : request.LocationName.Trim(),
            Latitude = request.Latitude ?? 40.4093,
            Longitude = request.Longitude ?? 49.8671,
            RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0),
            ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15,
        };
        _db.Locations.Add(location);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        var admin = new Employee
        {
            FullName = string.IsNullOrWhiteSpace(request.AdminName) ? "Admin" : request.AdminName.Trim(),
            // Login is by phone; the address only has to be unique within this tenant.
            Email = $"admin-{slug}@baki.local",
            PhoneNumber = phone,
            Role = EmployeeRole.Admin,
            LocationId = location.Id,
            PasswordHash = _passwordHasher.Hash(pin),
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow, // no activation link — the temp PIN is the credential
            MustChangePin = true,
        };
        _db.Employees.Add(admin);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await AuditAsync("TenantCreated", tenant.Id, slug,
            $"'{displayName}' — {slug}.qrlog.az, admin {phone}", HttpContext.RequestAborted);

        return Ok(new
        {
            id = tenant.Id,
            slug,
            host = $"{slug}.qrlog.az",
            adminPhone = phone,
            // Shown once. There is no way to read it back — only a reset.
            tempPin = pin,
        });
    }

    // PUT /api/super/tenants/{id}/active — disable a company without deleting anything. An inactive
    // tenant stops resolving (the Origin middleware only matches active ones), so its subdomain
    // stops working and its certificate stops renewing, while every row survives.
    [HttpPut("tenants/{id:guid}/active")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetActiveRequest request)
    {
        if (!await CanAsync(OperatorPermission.ManageTenants, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        // Locking the operator out of their own company would take the super-admin panel with it.
        if (!request.IsActive && tenant.Id == _tenant.TenantId)
            return BadRequest(new { error = "CannotDisableOwnTenant" });

        tenant.IsActive = request.IsActive;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        await AuditAsync(request.IsActive ? "TenantEnabled" : "TenantDisabled", tenant.Id, tenant.Slug,
            null, HttpContext.RequestAborted);
        return Ok(new { id = tenant.Id, isActive = tenant.IsActive });
    }

    // PUT /api/super/tenants/{id}/branding — display name, accent colour, logo.
    [HttpPut("tenants/{id:guid}/branding")]
    public async Task<IActionResult> SetBranding(Guid id, [FromBody] TenantBrandingRequest request)
    {
        if (!await CanAsync(OperatorPermission.ManageTenants, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            tenant.DisplayName = request.DisplayName.Trim();
            tenant.Name = tenant.DisplayName;
        }
        // Empty string clears (back to the built-in default); null leaves alone.
        if (request.Color is not null)
            tenant.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
        if (request.LogoUrl is not null)
            tenant.LogoKey = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        await AuditAsync("TenantBrandingChanged", tenant.Id, tenant.Slug,
            $"ad='{tenant.DisplayName}', rəng='{tenant.Color}'", HttpContext.RequestAborted);
        return Ok(new { id = tenant.Id, displayName = tenant.DisplayName, color = tenant.Color, logoUrl = tenant.LogoKey });
    }
}
