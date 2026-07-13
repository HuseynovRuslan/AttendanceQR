using AttendanceQR.Domain;

namespace AttendanceQR.Infrastructure.Multitenancy;

/// <summary>Per-request tenant. Resolved from the JWT (authenticated) or the request Origin/Host
/// (anonymous) and then used by the DbContext's query filter + insert stamping.</summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsResolved { get; }
    void Resolve(Guid tenantId);
}

public sealed class TenantContext : ITenantContext
{
    private Guid? _id;

    // Until resolved, fall back to the original tenant (Bakı Abadlıq). Safe while it is the ONLY
    // tenant; must be revisited before onboarding a second tenant so an unresolved request cannot
    // silently read tenant #1's data.
    public Guid TenantId => _id ?? TenantDefaults.BakiAbadligId;

    public bool IsResolved => _id.HasValue;

    public void Resolve(Guid tenantId) => _id = tenantId;
}
