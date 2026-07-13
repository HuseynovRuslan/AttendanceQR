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
}
