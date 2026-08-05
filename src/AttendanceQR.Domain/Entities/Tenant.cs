namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A customer company on the platform (multi-tenancy). Every tenant-scoped row carries a TenantId →
/// Tenant, and each tenant is reached at its own subdomain (<c>Slug</c>.qrlog.az). Phase 0 only
/// introduces the model + a single backfilled tenant; isolation (query filters) arrives in Phase 1.
/// </summary>
public class Tenant
{
    public Tenant()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    /// <summary>Internal/legal name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Subdomain slug — lowercase, unique, e.g. "bax", "eastcafe" → &lt;slug&gt;.qrlog.az.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Name shown in the app header / branding.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Optional branding: logo object key + primary colour (hex).</summary>
    public string? LogoKey { get; set; }

    public string? Color { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    // ── Commercial plan, limits and per-tenant feature flags (super-admin managed) ──────────────────

    /// <summary>The commercial package this company is on — "Start" / "Biznes" / "Korporativ" /
    /// "Enterprise". Informational + drives the price shown; null = not set.</summary>
    public string? Plan { get; set; }

    /// <summary>Soft caps for the plan. Null = unlimited. Surfaced as usage-vs-limit in the super-admin.
    /// Enforcement (blocking creation over the cap) is intentionally left to a later step so setting a
    /// limit can never break a tenant that is already over it.</summary>
    public int? MaxEmployees { get; set; }

    public int? MaxLocations { get; set; }

    /// <summary>Comma-separated feature keys that are turned OFF for this company (e.g.
    /// "facematch,payroll"). Empty/null = every feature on. Stored as an opt-OUT list so adding a new
    /// feature to the platform needs no migration and defaults to enabled everywhere.
    /// See <c>TenantFeatures</c> for the key catalogue and the resolver.</summary>
    public string? DisabledFeatures { get; set; }
}
