namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A platform-wide announcement from the operator — broadcast to EVERY tenant's employees at once (the
/// home banner), unlike <see cref="Announcement"/> which is scoped to a single company. GLOBAL (no tenant
/// query filter, like <see cref="SuperAdminAuditLog"/>): it belongs to the platform, not a tenant, so a
/// tenant admin can neither see it in their own list nor retire it — only the operator console can.
///
/// Deliberately simpler than the per-tenant Announcement: no audience targeting (a platform message —
/// "planned maintenance Sunday 02:00" — is for everyone) and no push, just the reliable home banner.
/// </summary>
public class GlobalAnnouncement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>Hold until this UTC time; null = live immediately.</summary>
    public DateTime? ScheduledForUtc { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
