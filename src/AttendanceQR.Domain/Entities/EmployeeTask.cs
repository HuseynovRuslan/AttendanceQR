namespace AttendanceQR.Domain.Entities;

/// <summary>Where an assigned task is in its lifecycle. Stored as int; values are stable, never renumber.</summary>
public enum EmployeeTaskStatus
{
    /// <summary>A manager assigned it; the worker has not finished it yet.</summary>
    Assigned = 0,

    /// <summary>The worker pressed «Hazırdır». Waiting for the manager to look at it.</summary>
    Done = 1,

    /// <summary>The manager accepted the work. The end of the line.</summary>
    Approved = 2,

    /// <summary>Called off — the work is no longer wanted. Kept rather than deleted so the worker
    /// who already saw it understands where it went.</summary>
    Cancelled = 3,
}

/// <summary>
/// A job a manager gives a specific worker: "clean the third floor", "take the van for service".
/// Distinct from <see cref="TaskItem"/>, which is the operator team's own private to-do list and is
/// not tenant-scoped at all — this one belongs to a customer company and is the thing their managers
/// and their staff actually use.
///
/// The lifecycle is deliberately short — Assigned → Done → Approved — because the audience is a
/// cleaner or a driver with one hand free. An "in progress" state would be a third tap nobody makes,
/// and a task that is half-done reads the same as one not started.
///
/// Nothing here touches attendance. A task is not presence: finishing one does not record a check-in,
/// and leaving one undone never costs a day's pay. That separation is the whole reason this is its
/// own entity instead of a field on <see cref="FieldVisit"/>.
/// </summary>
public class EmployeeTask : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Multi-tenancy: which company this row belongs to (auto-stamped on save).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The worker who must do it.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>The manager/admin who assigned it.</summary>
    public Guid AssignedByEmployeeId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Optional detail — where exactly, what to bring, who to ask for.</summary>
    public string? Description { get; set; }

    /// <summary>Optional deadline. Null = "when you can". Overdue is derived, never stored, so it is
    /// always true of today rather than of whenever a job last ran.</summary>
    public DateOnly? DueDate { get; set; }

    public EmployeeTaskStatus Status { get; set; } = EmployeeTaskStatus.Assigned;

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the worker pressed «Hazırdır».</summary>
    public DateTime? DoneAtUtc { get; set; }

    /// <summary>What the worker typed when finishing — "the pump was already replaced", "door was
    /// locked, will return tomorrow". Optional, and the reason a rejected task is rarely a surprise.</summary>
    public string? WorkerNote { get; set; }

    /// <summary>Object key of the proof photo the worker may attach (its own <c>tasks/</c> prefix, so
    /// neither the selfie retention job nor the face-match worker ever touches it — this is a picture
    /// of WORK, not of a person). Null when none was taken; a task is never blocked on a photo.</summary>
    public string? PhotoKey { get; set; }

    public DateTime? PhotoAtUtc { get; set; }

    /// <summary>Who accepted it, and when. Set on approval; cleared if the manager sends it back.</summary>
    public Guid? ApprovedByEmployeeId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>Why the manager sent it back, shown to the worker on the task they must redo.</summary>
    public string? RejectionNote { get; set; }
}
