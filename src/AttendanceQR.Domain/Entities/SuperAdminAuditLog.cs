namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A record of every action taken in the platform super-admin — the one place that crosses tenants.
/// GLOBAL, not tenant-scoped: it spans companies, so (like the shared task board) it carries no
/// TenantId and no query filter. Append-only in practice — there is no edit or delete path. This is
/// the compliance baseline for a multi-tenant back-office: no super-admin mutation should go unlogged.
/// </summary>
public class SuperAdminAuditLog
{
    public SuperAdminAuditLog()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    /// <summary>The super-admin who acted — an employee id from the config allowlist.</summary>
    public Guid ActorEmployeeId { get; set; }

    /// <summary>Their full name at the time, denormalised so the log reads without a join even if the
    /// employee row later changes.</summary>
    public string ActorName { get; set; } = string.Empty;

    /// <summary>A stable action code, e.g. "TenantCreated", "TenantDisabled", "TenantBrandingChanged".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The company acted on, when the action targets one.</summary>
    public Guid? TargetTenantId { get; set; }

    /// <summary>The company's slug at the time, for a readable log without a join.</summary>
    public string? TargetTenantSlug { get; set; }

    /// <summary>Free-form human-readable detail / before→after summary.</summary>
    public string? Details { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
