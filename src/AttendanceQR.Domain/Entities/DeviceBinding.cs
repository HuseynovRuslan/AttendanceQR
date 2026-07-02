namespace AttendanceQR.Domain.Entities;

public class DeviceBinding
{
    public DeviceBinding()
    {
        Id = Guid.NewGuid();
        BoundAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // FK to Employee, unique (1-to-1).
    public Guid EmployeeId { get; set; }

    public string DeviceFingerprint { get; set; } = string.Empty;

    public DateTime BoundAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
