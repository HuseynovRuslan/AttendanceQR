using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A task one person (the assigner — typically a rəhbər/Admin) hands to another (the assignee — a
/// Manager / field supervisor). The assignee marks it done; both sides are notified across the two
/// state changes (assigned → the assignee is told; completed → the assigner is told).
///
/// There is no persisted "notification" row: the admin bell computes task alerts live from these
/// columns (a Pending task assigned to you, a Completed task you assigned and have not acknowledged),
/// mirroring how the rest of the bell works. Web Push is fired best-effort on top for phones.
/// </summary>
public class TaskItem : ITenantScoped
{
    public TaskItem()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to. Auto-stamped on insert.
    public Guid TenantId { get; set; }

    // Who gave the task (rəhbər) and who has to do it (menecer / sahə nəzarətçisi).
    public Guid AssignedByEmployeeId { get; set; }
    public Guid AssignedToEmployeeId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    // Optional soft deadline — shown to the assignee, never enforced.
    public DateOnly? DueDate { get; set; }

    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }

    // Set when the assignee marks it done.
    public DateTime? CompletedAtUtc { get; set; }

    // Set when the assigner has seen the completion — this is what clears the "tamamlandı" bell item,
    // since a completed task otherwise stays completed (and would alert) forever.
    public DateTime? AcknowledgedByAssignerAtUtc { get; set; }
}
