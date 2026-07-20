using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An admin-authored message broadcast to employees in the tenant — shown as a banner on the home
/// screen and in the employee notifications feed. Can carry an optional title, target a subset of
/// staff (see <see cref="Audience"/>), and be scheduled to appear later. Tenant-scoped.
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

    // Optional heading shown above the message.
    public string? Title { get; set; }

    public string Message { get; set; } = string.Empty;

    // Who sees it. Selected uses the Recipients list; the others are computed at read time.
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.All;

    // When it starts being visible. Null = immediately. Employees only see it once now >= this.
    public DateTime? ScheduledForUtc { get; set; }

    // Soft on/off so an admin can retire a message without deleting it (and without it vanishing from
    // anyone mid-read). Employees only ever fetch active ones.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    // Only used when Audience == Selected.
    public ICollection<AnnouncementRecipient> Recipients { get; set; } = new List<AnnouncementRecipient>();
}

/// <summary>One targeted recipient of a <see cref="Announcement"/> whose Audience is Selected.</summary>
public class AnnouncementRecipient : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid AnnouncementId { get; set; }

    public Guid EmployeeId { get; set; }
}
