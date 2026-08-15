namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A shared to-do item for a company's own team board — «Tapşırıqlar».
///
/// It used to be GLOBAL and gated on a five-person employee-id allowlist, on the reasoning that the
/// same handful of people ran every company. That reasoning stopped holding the moment the board was
/// wanted by a whole company's admins: opening the gate on a global table would have shown CleanFix's
/// and EastCaf's admins the operator team's own list — «Ödəniş sisteminin qurulması (Odero,
/// Payriff)», «KOBİA sənədləri» — which is internal business, not a customer's to read.
///
/// So it is tenant-scoped now, like everything else that belongs to a company, and the allowlist is
/// gone: every admin and manager of a company shares that company's board and sees no other.
/// </summary>
public class TaskItem : ITenantScoped
{
    public TaskItem()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    /// <summary>Multi-tenancy: which company this board belongs to (auto-stamped on save).</summary>
    public Guid TenantId { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsDone { get; set; }

    /// <summary>Starred as important (Microsoft To Do's star).</summary>
    public bool IsImportant { get; set; }

    /// <summary>Optional due date (Microsoft To Do's "Bu gün / Sabah / Tarix seç"). Null = no date.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Manual sort position within the open list (drag to reorder). Lower = higher up.</summary>
    public double SortOrder { get; set; }

    /// <summary>Who added it — id + a denormalised name so the board can show "əlavə etdi" without a
    /// tenant-scoped join back to Employees (the author may be in a different tenant than the viewer).</summary>
    public Guid CreatedByEmployeeId { get; set; }

    public string CreatedByName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? DoneAtUtc { get; set; }
}
