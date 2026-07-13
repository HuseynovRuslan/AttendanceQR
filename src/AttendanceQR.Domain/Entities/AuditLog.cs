using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class AuditLog
{
    public AuditLog()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    // Nullable — some events may not be tied to a known employee.
    public Guid? EmployeeId { get; set; }

    public AuditEventType EventType { get; set; }

    // Rejection reason, when applicable.
    public string? Reason { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
