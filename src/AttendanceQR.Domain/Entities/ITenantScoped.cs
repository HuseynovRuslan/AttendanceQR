namespace AttendanceQR.Domain.Entities;

/// <summary>Marks an entity that belongs to a single tenant. Used to auto-stamp TenantId on insert and
/// (implicitly) to apply the per-tenant query filter.</summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
