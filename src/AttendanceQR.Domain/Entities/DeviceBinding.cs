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

    // Human-friendly device name derived client-side from the User-Agent at activation (e.g.
    // "Samsung Galaxy", "iPhone") — shown in the admin employee list. Never used for any security
    // decision; DeviceFingerprint is the only value scan actually matches against.
    public string? DeviceLabel { get; set; }

    public DateTime BoundAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
