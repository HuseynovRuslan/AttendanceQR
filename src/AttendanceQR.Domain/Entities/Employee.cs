using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class Employee
{
    public Employee()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public EmployeeRole Role { get; set; }

    public Guid LocationId { get; set; }

    // Admin kill-switch: whether the account is enabled. Independent of activation.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    // Invitation/activation. Only the SHA256 hash of the activation token is stored —
    // the plaintext token is never persisted. Nulled out once the account is activated.
    public string? InvitationTokenHash { get; set; }

    public DateTime? InvitationExpiresUtc { get; set; }

    // Null until the employee completes activation (sets password + binds device).
    public DateTime? ActivatedAtUtc { get; set; }

    // 1-to-1, nullable — an employee may not yet have a bound device.
    public DeviceBinding? DeviceBinding { get; set; }
}
