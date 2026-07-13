namespace AttendanceQR.Domain.Entities;

/// <summary>
/// Many-to-many link: which locations a manager oversees. A manager may manage several locations;
/// report/export scope is restricted to this set. Composite key (EmployeeId, LocationId).
/// </summary>
public class ManagedLocation
{
    public Guid EmployeeId { get; set; }

    public Guid LocationId { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }
}
