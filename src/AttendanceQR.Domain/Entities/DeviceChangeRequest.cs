using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class DeviceChangeRequest
{
    public DeviceChangeRequest()
    {
        Id = Guid.NewGuid();
        RequestedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    // FK to the Employee who requested the change.
    public Guid EmployeeId { get; set; }

    public string NewDeviceFingerprint { get; set; } = string.Empty;

    public DeviceChangeStatus Status { get; set; } = DeviceChangeStatus.Pending;

    public DateTime RequestedAtUtc { get; set; }

    // FK to the Employee (Manager/Admin) who reviewed it — null while pending.
    public Guid? ReviewedByEmployeeId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
}
