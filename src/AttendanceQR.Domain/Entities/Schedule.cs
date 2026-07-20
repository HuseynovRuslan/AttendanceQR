namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A named, reusable work schedule (qrafik) — a template of shift hours the admin can pick when
/// creating or editing a location, instead of re-typing the times each time. "Gündüz" (09:00–18:00),
/// "Gecə" (22:00–06:00), and any custom ones.
///
/// Deliberately a TEMPLATE, not a live reference: picking a schedule copies its values onto the
/// location's own shift fields, which stay the source of truth for scan/late/overtime. So editing a
/// schedule does not silently change how a live location computes attendance — that keeps the scan and
/// report paths untouched. (A managed/propagating model was considered and rejected for that reason.)
/// </summary>
public class Schedule : ITenantScoped
{
    public Schedule()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    /// <summary>What the admin calls it in the picker, e.g. "Gündüz" or "Gecə növbəsi".</summary>
    public string Name { get; set; } = string.Empty;

    public TimeOnly ShiftStart { get; set; }

    /// <summary>Earlier than <see cref="ShiftStart"/> means an overnight shift (crosses midnight) —
    /// the same convention locations use.</summary>
    public TimeOnly ShiftEnd { get; set; }

    public int LateThresholdMinutes { get; set; } = 15;

    /// <summary>Working-days bitmask, same layout as Location.WorkDaysMask (Sunday=0 … Saturday=6).
    /// Default 126 = every day except Sunday.</summary>
    public int WorkDaysMask { get; set; } = 126;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
