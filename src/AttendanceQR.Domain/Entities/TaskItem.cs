namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A shared to-do item for the operator team's task board. GLOBAL — deliberately NOT tenant-scoped:
/// the same handful of people run every company, so their task list is one shared list, not one per
/// tenant. Access is a config employee-id allowlist (App:TaskBoardEmployeeIds), the same pattern as
/// the super-admin panel — a role lives inside a tenant, this list spans them.
/// </summary>
public class TaskItem
{
    public TaskItem()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    /// <summary>Who added it — id + a denormalised name so the board can show "əlavə etdi" without a
    /// tenant-scoped join back to Employees (the author may be in a different tenant than the viewer).</summary>
    public Guid CreatedByEmployeeId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? DoneAtUtc { get; set; }
}
