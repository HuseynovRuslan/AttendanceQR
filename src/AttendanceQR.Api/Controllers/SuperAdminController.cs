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

    /// <summary>
    /// Runs a block of work scoped to ONE customer's tenant, and puts it back afterwards.
    ///
    /// Everything on /api/super runs as the operator, whose own tenant is whichever company their
    /// employee row lives in. Writing a row into a CUSTOMER therefore means moving the request's
    /// tenant scope by hand first, because AppDbContext stamps TenantId from whatever is in scope at
    /// SaveChanges — silently, with no error to notice. A super endpoint that forgets, or that
    /// resolves the wrong id, writes one company's branches or staff into another company's data.
    ///
    /// So there is exactly one Resolve() call on this surface and it is here, where a reviewer can
    /// find it by grepping for the name. The scope is restored on the way out so a later part of the
    /// same request cannot inherit the customer's tenant by accident.
    /// </summary>
    private async Task<T> WithTenantAsync<T>(Guid tenantId, Func<Task<T>> body)
    {
        var previous = _tenant.IsResolved ? _tenant.TenantId : (Guid?)null;
        _tenant.Resolve(tenantId);
        try
        {
            return await body();
        }
        finally
        {
            if (previous is Guid back) _tenant.Resolve(back);
        }
    }

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

        // Whether the company's admin account belongs to anybody yet. A company is now built before it
        // has an owner, and the account it is built through has no phone and no email until handover —
        // so this is the difference between "ready to hand over" and "handed over", and the console
        // says which on the row rather than leaving the operator to remember.
        var claimedAdmins = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.IsActive && e.Role == EmployeeRole.Admin && (e.PhoneNumber != null || e.Email != null))
            .Select(e => e.TenantId)
            .Distinct()
            .ToListAsync(ct);

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
            hasAdmin = claimedAdmins.Contains(t.Id),
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

        // The customer's admin no longer has to be named here. The operator sets a company up before
        // handing it over — branches, staff, shifts — and at minute zero they often do not yet know
        // which person at the customer will hold the account, or their number. Leaving the phone out
        // creates the admin WITHOUT a way to sign in: no phone, no email, and a password nobody
        // holds. It exists so the company has an admin to configure it through (impersonation needs
        // one), and POST tenants/{id}/admin gives it to a real person at handover.
        var wantsCredentials = !string.IsNullOrWhiteSpace(request.AdminPhone);
        string? phone = null;
        if (wantsCredentials)
        {
            phone = PhoneNumbers.Normalize(request.AdminPhone);
            if (phone is null)
                return BadRequest(new { error = "AdminPhoneInvalid" });
        }

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

        // Everything from here belongs to the NEW company, so it runs inside the target's tenant
        // scope — see WithTenantAsync for what happens to a super endpoint that forgets.
        var admin = await WithTenantAsync(tenant.Id, async () =>
        {
            var starterLocation = new Location
            {
                Name = string.IsNullOrWhiteSpace(request.LocationName) ? "Baş ofis" : request.LocationName.Trim(),
                // Baku, until the admin drops a map link on it. A branch at the wrong coordinates
                // refuses every scan as "outside the area", so this is the first thing to fix.
                Latitude = request.Latitude ?? 40.4093,
                Longitude = request.Longitude ?? 49.8671,
                RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15,
            };
            _db.Locations.Add(starterLocation);

            // The shift picker is empty without these, and it used to fill only on the next backend
            // restart — after the company had already been set up and handed over.
            _db.Schedules.AddRange(DefaultSchedules.For(tenant.Id));
            await _db.SaveChangesAsync(HttpContext.RequestAborted);

            var firstAdmin = new Employee
            {
                FullName = string.IsNullOrWhiteSpace(request.AdminName) ? "Admin" : request.AdminName.Trim(),
                // No email. This used to synthesise "admin-{slug}@baki.local" — an address at a domain
                // that does not exist, carrying the name of the first customer, shown back to the
                // operator as if it were something the account owned. Login is by phone, and the
                // ordinary create-employee path already keeps a null email for a phone-only employee
                // ("no synthesised placeholder", AdminController). The unique index is (TenantId,
                // Email) and Postgres treats NULLs as distinct, so any number of them coexist.
                Email = null,
                PhoneNumber = phone,
                Role = EmployeeRole.Admin,
                LocationId = starterLocation.Id,
                // With no phone there is no way to sign in as this account at all: login matches a
                // phone or an email, and it has neither. The hash is a random one nobody is told, so
                // the row is an anchor for the operator to configure through and nothing else, until
                // POST tenants/{id}/admin gives it to a real person.
                PasswordHash = _passwordHasher.Hash(wantsCredentials ? pin : PinRules.Generate()),
                IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow, // no activation link — the temp PIN is the credential
                MustChangePin = true,
            };
            _db.Employees.Add(firstAdmin);
            await _db.SaveChangesAsync(HttpContext.RequestAborted);
            return firstAdmin;
        });

        await AuditAsync("TenantCreated", tenant.Id, slug,
            $"'{displayName}' — admin {phone ?? "(təyin edilməyib)"}", HttpContext.RequestAborted);

        return Ok(new
        {
            id = tenant.Id,
            slug,
            displayName,
            host = $"{slug}.qrlog.az",
            adminId = admin.Id,
            adminPhone = phone,
            // Shown once. There is no way to read it back — only a reset. Null when no phone was
            // given: nothing was issued, because there is nobody yet to issue it to.
            tempPin = wantsCredentials ? pin : null,
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
