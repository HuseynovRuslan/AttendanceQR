namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One change to the plan, as a whole: who made it, when, and what every touched day was before and after. It is
/// the plan's history («who moved me to nights, and from what?») and what «Geri al» undoes — an undo is a change of
/// its own, pointing back at the one it reverses.
/// </summary>
public class ShiftPlanChange : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>The branch it was made from, for that branch's history list.</summary>
    public Guid? LocationId { get; set; }

    public Guid? CreatedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>One line: «12 işçi, 14–20 noyabr: Gecə».</summary>
    public string Summary { get; set; } = string.Empty;

    public int People { get; set; }

    public int Days { get; set; }

    /// <summary>Set on an undo: the change it reversed.</summary>
    public Guid? UndoOfId { get; set; }

    /// <summary>Set on a change that was undone.</summary>
    public DateTime? UndoneAtUtc { get; set; }

    public Guid? UndoneByEmployeeId { get; set; }
}

/// <summary>
/// One person's one day in a <see cref="ShiftPlanChange"/> — or, for a standing shift, the day it starts.
/// <see cref="Before"/> and <see cref="After"/> are what the day's own plan was: a shift's id, <c>rest</c>, or null for
/// «no plan, the person's own shift». The labels are the shift's name and hours as they were, so the history still
/// reads right after a shift is renamed or re-timed.
/// </summary>
public class ShiftPlanChangeItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid ChangeId { get; set; }

    public Guid EmployeeId { get; set; }

    public DateOnly Date { get; set; }

    /// <summary>A standing shift (<see cref="ShiftAssignment"/>) rather than one day.</summary>
    public bool Standing { get; set; }

    public string? Before { get; set; }

    public string? After { get; set; }

    public string BeforeLabel { get; set; } = string.Empty;

    public string AfterLabel { get; set; } = string.Empty;

    /// <summary>For a standing change: the person's assignments before and after, so an undo puts them back exactly.</summary>
    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }
}

/// <summary>
/// A worked day whose stored summary has to be rebuilt because its plan changed. Written in the same save as the
/// plan, so the two cannot disagree for long: if the rebuild right after the save fails, the row stays and the
/// rebuild worker tries again until it holds. Until then the tabel shows the day as it was — never half of it.
/// </summary>
public class PendingSummaryRebuild : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public DateOnly Date { get; set; }

    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    public int Attempts { get; set; }
}
