namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An admin-authored message broadcast to every employee in the tenant — shown as a banner on the
/// home screen and in the employee notifications feed. There was no employee-facing broadcast channel
/// before this (the notifications feed was only the employee's own check-ins). Tenant-scoped.
/// </summary>
public class Announcement : ITenantScoped
{
    public Announcement()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Message { get; set; } = string.Empty;

    // Soft on/off so an admin can retire a message without deleting it (and without it vanishing from
    // anyone mid-read). Employees only ever fetch active ones.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
