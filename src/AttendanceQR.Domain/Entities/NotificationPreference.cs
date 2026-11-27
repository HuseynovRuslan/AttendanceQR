using AttendanceQR.Domain.Notifications;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One person's notification settings for one category («Bildiriş ayarları»), kept on the server in the company's
/// context — a phone change or a second device sees the same settings.
///
/// No row = everything off. <see cref="EnabledTypes"/> is a CSV of <see cref="NotificationTypes"/> keys (the way
/// <c>Tenant.DisabledFeatures</c> stores its keys), so a new type needs no migration. The settings say what the person
/// WANTS; what they may receive is decided again at every send by their current scope — switching a type on never
/// widens anybody's access, and a revoked scope stops the notifications with it.
/// </summary>
public class NotificationPreference : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary><see cref="NotificationTypes.Categories"/>.</summary>
    public string Category { get; set; } = NotificationTypes.CategoryAttendance;

    public string EnabledTypes { get; set; } = string.Empty;

    public NotificationBranchMode BranchMode { get; set; } = NotificationBranchMode.AllPermitted;

    /// <summary>A line in the notification center.</summary>
    public bool InApp { get; set; } = true;

    /// <summary>A push to the person's phones (the app or a browser with notifications on).</summary>
    public bool Push { get; set; } = true;

    /// <summary>A live pop-up in an open admin panel.</summary>
    public bool Web { get; set; } = true;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A branch picked under «Yalnız seçilmiş filiallar» (<see cref="NotificationBranchMode.Selected"/>).</summary>
public class NotificationBranchPreference : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string Category { get; set; } = NotificationTypes.CategoryAttendance;
    public Guid LocationId { get; set; }
}
