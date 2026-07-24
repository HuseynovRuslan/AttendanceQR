namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A named shift ("növbə") — hours, working days, and an optional rotation, defined once and assigned
/// to people. "Gündüz", "Gecə A", "Gecə B", and any custom one.
///
/// It began as a template that merely copied its hours onto a location, and that is why the three
/// companies ended up with a "Gecə növbəsi" row saying 22:00–06:00 while the eight people actually
/// working nights were on 21:00–07:00, plus a duplicate "gece" nobody noticed. Copying loses the
/// link, and once the link is lost the library drifts from reality — the same disease the job-title
/// catalogue was built to cure.
///
/// So it is now a LIVE reference: <see cref="Employee.ScheduleId"/> points here, and every screen
/// resolves an employee's hours and days through it. The consequence has to be understood before
/// editing one: changing a shift's hours changes how PAST days are reported too, because reports are
/// recomputed from the schedule rather than from a copy taken at the time. That is the accepted
/// trade — versioning shifts would be a much larger feature — and the admin UI says so out loud.
///
/// Assigning a shift is optional. An employee with no ScheduleId keeps the older behaviour: their own
/// WorkStart/WorkEnd if set, otherwise the location's. See AttendanceQR.Application EffectiveShift,
/// the single place that decides between the three.
/// </summary>
public class Schedule : ITenantScoped, IHasWorkCycle
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
    /// Default 126 = every day except Sunday. Ignored when <see cref="WorkCycleDays"/> is set — a
    /// rotation replaces the weekly calendar rather than layering on it.</summary>
    public int WorkDaysMask { get; set; } = 126;

    /// <summary>
    /// Rotation, same three fields and same meaning as on <see cref="Employee"/>: cycle length, how
    /// many of its first days are worked, and one date the shift is known to be ON. Null length = no
    /// rotation, the weekly mask decides.
    ///
    /// The anchor lives HERE rather than on the employee on purpose. Two crews alternating on the
    /// same rotation are two shifts — "Gecə A" and "Gecə B", anchored a day apart — which is how a
    /// manager already talks about them, and it means assigning someone is one choice rather than a
    /// choice plus a date they have to get right.
    /// </summary>
    public int? WorkCycleDays { get; set; }

    public int WorkCycleOnDays { get; set; } = 1;

    public DateOnly? WorkCycleAnchor { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
