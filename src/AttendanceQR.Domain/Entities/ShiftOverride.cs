namespace AttendanceQR.Domain.Entities;

/// <summary>
/// «Əvəzləmə» — one person works a DIFFERENT shift on ONE date, and then goes back to their own.
///
/// A shift is assigned to a PERSON, which is right for the ninety-nine days out of a hundred they
/// work their own hours — and wrong for the hundredth. Nəcəfov Vüqar is on «FM 2-ci növbə
/// 13:00–23:00»; on Saturday 5 September he covered the night guard, arrived at 21:33 and left at
/// 06:51 the next morning. The system had nowhere to record that, so it judged the night against his
/// own day shift: the overnight rule never applied (his shift ends AFTER it starts, so it is not an
/// overnight one), the Saturday record stayed open at zero hours, and the morning exit scan opened a
/// fresh check-in on Sunday — which was his rest day. He lost the nine hours he worked and the rest
/// day he was owed, in one night.
///
/// Deliberately a POINTER TO A SCHEDULE rather than a pair of times. Hours typed onto a day are a
/// copy, and a copy drifts — the same disease <see cref="Schedule"/> itself was built to cure, where
/// three companies ended up with a «Gecə növbəsi» row that disagreed with the eight people working
/// nights. Naming the shift means «that night he was on Gecə A», which is both what happened and what
/// the rota says.
///
/// It changes nothing about how a day is calculated except WHICH schedule answers for it, so every
/// rule downstream — the overnight noon-pivot, the working-day mask, the late threshold, per-day
/// hours — applies unchanged. That is the whole reason this is the shape it is.
/// </summary>
public class ShiftOverride : ITenantScoped
{
    public ShiftOverride()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Whose day this replaces.</summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// The date the override applies to, in COMPANY time — the same date an AttendanceRecord is
    /// stamped with. For a night that crosses midnight this is the day the shift BEGINS, because that
    /// is the day the record belongs to.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>The shift they actually worked that day.</summary>
    public Guid ScheduleId { get; set; }

    /// <summary>Why, in the admin's words — «mühafizəçini əvəz etdi». Optional, and never parsed.</summary>
    public string? Note { get; set; }

    /// <summary>Who recorded it. A day's hours changed by hand needs a name against it.</summary>
    public Guid? CreatedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
