namespace AttendanceQR.Domain.Entities;

/// <summary>What a reminder was about. Also the dedupe key together with the employee and the day.</summary>
public enum EmployeeNotificationType
{
    /// <summary>Shift starts soon and there's no check-in yet.</summary>
    CheckInSoon = 0,
    /// <summary>Shift ends soon and they're still checked in.</summary>
    CheckOutSoon = 1,
    /// <summary>Shift is over and no check-out was ever recorded.</summary>
    MissedCheckOut = 2,
}

/// <summary>
/// A reminder that was sent to one employee. Serves two jobs at once: the (EmployeeId, Type,
/// RelatedDate) unique index is what stops the job sending the same nudge twice, and the rows are the
/// employee's notification inbox — a push that is only ever a phone banner is gone the moment it's
/// swiped away, so the app has nothing to show. Tenant-scoped.
/// </summary>
public class EmployeeNotification : ITenantScoped
{
    public EmployeeNotification()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public EmployeeNotificationType Type { get; set; }

    /// <summary>The work day this reminder was about — the per-day half of the dedupe key.</summary>
    public DateOnly RelatedDate { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
