using AttendanceQR.Domain.Notifications;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// The transactional outbox: one row per thing that happened, written in the SAME SaveChanges as the thing itself — a
/// check-in row and its event commit together or not at all, so an event can never be lost between the two, and
/// nothing is sent about a check-in that was refused.
///
/// Deliberately inert so it can never fail a check-in: no unique index, no foreign key but the tenant's, short
/// bounded columns. Duplicates are not prevented here but where they would matter — one <see cref="Notification"/>
/// per (event, recipient, type). Read and processed by <c>NotificationWorker</c>; kept for a week once done.
/// </summary>
public class NotificationEvent : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary><see cref="NotificationEventKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The person it is about.</summary>
    public Guid SubjectEmployeeId { get; set; }

    public Guid? AttendanceRecordId { get; set; }

    /// <summary>Where it happened (the scanned branch), shown as «Filial».</summary>
    public Guid? LocationId { get; set; }

    /// <summary>When it actually happened — the scan's own moment, also for an offline scan synced later.</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Recorded offline and synced later.</summary>
    public bool WasOffline { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public NotificationEventStatus Status { get; set; } = NotificationEventStatus.Pending;
    public int Attempts { get; set; }

    /// <summary>When the worker may (next) take it — also its lease while one pass works on it.</summary>
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAtUtc { get; set; }
    public string? LastError { get; set; }
}
