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

    /// <summary>
    /// The resolved tenant. FAIL-CLOSED: reading this before anything resolved it throws rather than
    /// falling back to a default. Until there was a second tenant a fallback was harmless; with
    /// several live tenants it is the opposite — an unresolved request would silently read and, worse,
    /// WRITE tenant #1's data (SaveChanges stamps rows with whatever this returns). Every legitimate
    /// entry point resolves explicitly: the JWT (OnTokenValidated), the Origin/Host middleware, the
    /// per-tenant loop in DailySummaryJob/FaceMatchWorker, and the startup seed scope.
    /// </summary>
    public Guid TenantId => _id ?? throw new InvalidOperationException(
        "Tenant is not resolved for this scope. An authenticated request resolves it from the JWT 'tid' " +
        "claim; an anonymous one from the Origin/Host subdomain; background work must Resolve() the " +
        "tenant it is processing. Reaching tenant-scoped data without one is a bug, not a default.");

    public bool IsResolved => _id.HasValue;

    public void Resolve(Guid tenantId) => _id = tenantId;
}
