namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One line of a field visit's work checklist — "Zibili boşalt". The manager writes the lines when
/// assigning the visit; only that visit's OWN worker ticks them (a manager tick would be worthless as
/// evidence, and a manager who could edit ticks would let the board lie).
///
/// Ticking is an absolute SET, never a toggle, so a retried or replayed tick is a no-op. An unticked
/// item NEVER blocks the check-out — it flags the visit for the manager, exactly like a far GPS.
/// <c>DoneAtUtc</c> per item is the point of storing them separately: "09:40 / 10:15 / 11:02" and
/// "all four ticked at 17:59 on the way out" are different stories, and that difference is why a
/// manager opens the visit at all.
///
/// Shaped like <see cref="AnnouncementRecipient"/>: indexed parent id, no navigation property and no
/// FK to FieldVisits.
/// </summary>
public class FieldVisitChecklistItem : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Multi-tenancy: which company (Tenant) this row belongs to. Auto-stamped on save.
    public Guid TenantId { get; set; }

    public Guid FieldVisitId { get; set; }

    /// <summary>The instruction as the manager typed it. Trimmed and capped by the controller.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>0-based; the manager's order is the display order and the tick order.</summary>
    public int SortOrder { get; set; }

    public bool IsDone { get; set; }

    /// <summary>When it was ticked — null while unticked. Cleared again if the worker unticks.</summary>
    public DateTime? DoneAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
