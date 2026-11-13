namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A person's standing shift over a stretch of time: one shift every day, or a rotation that repeats from
/// <see cref="From"/> («2 day, 2 night, 2 off» is six steps), with no end or until <see cref="Until"/>.
///
/// It exists so the past keeps the shift it was worked on. A standing shift used to be one column
/// (<see cref="Employee.ScheduleId"/>), so moving somebody from mornings to nights re-judged every earlier day the
/// next time it was recomputed — last month's tabel, measured by this month's shift. Now a change closes the
/// assignment that was running (until yesterday) and opens the new one, and a day is resolved by the assignment
/// that covered it (AttendanceQR.Application ShiftOverrideMap). A person with no assignment at all — everybody
/// before 2026-11-12 — is still resolved by <see cref="Employee.ScheduleId"/>; their first change writes what they
/// were on as the assignment up to that day, so nothing earlier moves.
///
/// A day planned on its own (<see cref="ShiftOverride"/>) still wins over this for that day.
/// </summary>
public class ShiftAssignment : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The first day, and the day a rotation is counted from.</summary>
    public DateOnly From { get; set; }

    /// <summary>The last day; null for no end.</summary>
    public DateOnly? Until { get; set; }

    /// <summary>
    /// The days in turn, comma-separated: a shift's id, or <c>rest</c>. Empty: the person's own hours or their
    /// branch's (somebody who was on no named shift).
    /// </summary>
    public string Steps { get; set; } = string.Empty;

    public Guid? CreatedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public const string Rest = "rest";

    public bool Covers(DateOnly date) => From <= date && (Until is null || date <= Until);

    /// <summary>The step for <paramref name="date"/>: a shift id, <see cref="Rest"/>, or empty.</summary>
    public string StepOn(DateOnly date)
    {
        var steps = StepList();
        if (steps.Length == 0)
            return string.Empty;
        var offset = date.DayNumber - From.DayNumber;
        return steps[((offset % steps.Length) + steps.Length) % steps.Length];
    }

    /// <summary>The first shift in the steps: whose hours a rest day in this assignment keeps.</summary>
    public Guid? FirstShift() => StepList().Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null).FirstOrDefault(id => id is not null);

    public string[] StepList() => string.IsNullOrWhiteSpace(Steps)
        ? []
        : Steps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
