namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One notification for one person — a line of their notification center («Bildirişlər»), read or unread.
///
/// The text is written when it is created (a renamed branch or person does not rewrite history), and so are the fields
/// the screen filters and groups by. <see cref="LocationId"/> is the SCOPE branch — the subject's own branch, the one
/// every scope rule in the system keys on — and the center only lists notifications whose branch the reader may still
/// see: a manager moved off a branch stops seeing its history too. <see cref="BranchName"/> is where it happened.
/// </summary>
public class Notification : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid RecipientEmployeeId { get; set; }

    /// <summary>The outbox event it came from; with the type and the recipient, unique — the duplicate guard.</summary>
    public Guid? EventId { get; set; }

    /// <summary><see cref="Notifications.NotificationTypes"/>.</summary>
    public string TypeKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public Guid? SubjectEmployeeId { get; set; }
    public string? SubjectName { get; set; }

    /// <summary>The scope branch (the subject's own). Null = not tied to a branch.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Where it happened, as it was called then.</summary>
    public string? BranchName { get; set; }

    /// <summary>When it happened — for an offline scan, the scan's moment, not the sync's.</summary>
    public DateTime OccurredAtUtc { get; set; }

    public bool WasOffline { get; set; }

    /// <summary>Minutes late, for a late arrival.</summary>
    public int? Minutes { get; set; }

    /// <summary>Where a click goes — checked again by that screen's own scope rule.</summary>
    public string? Url { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}
