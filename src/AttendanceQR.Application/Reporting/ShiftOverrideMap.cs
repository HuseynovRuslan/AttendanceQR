using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Which schedule answers for one person on one date, once «əvəzləmə» is taken into account.
///
/// It is a lookup and nothing else, and that is the point. An override does not introduce a new kind
/// of shift or a new branch in the day's arithmetic — it only changes WHICH <c>Schedule</c> the
/// existing resolver is handed. Everything downstream then applies unchanged: the overnight
/// noon-pivot, the working-day mask, the late threshold, per-day hours. A cover night becomes an
/// ordinary night the moment the right shift answers for it.
///
/// Loaded once per query rather than looked up per row — the reporting screens walk hundreds of
/// employee-days and a per-row round trip is how a month's tabel becomes a minute.
/// </summary>
public sealed class ShiftOverrideMap
{
    /// <summary>No overrides at all — what every caller uses until it loads some.</summary>
    public static readonly ShiftOverrideMap Empty = new(new Dictionary<(Guid, DateOnly), Guid>());

    private readonly Dictionary<(Guid EmployeeId, DateOnly Date), Guid> _byEmployeeDay;

    public ShiftOverrideMap(Dictionary<(Guid, DateOnly), Guid> byEmployeeDay)
        => _byEmployeeDay = byEmployeeDay;

    public bool IsEmpty => _byEmployeeDay.Count == 0;

    /// <summary>The schedule that replaces this person's own on this date, or null.</summary>
    public Guid? On(Guid employeeId, DateOnly date)
        => _byEmployeeDay.TryGetValue((employeeId, date), out var id) ? id : null;

    /// <summary>
    /// The schedule id to resolve the day with: the override if there is one, otherwise the person's
    /// own. This is the ONE line every call site changes, which is what keeps the eight places that
    /// resolve a shift agreeing with each other.
    /// </summary>
    public Guid? EffectiveScheduleId(Guid employeeId, DateOnly date, Guid? ownScheduleId)
        => On(employeeId, date) ?? ownScheduleId;

    /// <summary>
    /// The schedule object itself, resolved through the override. Every call site is one line, which is
    /// the only way eight of them stay in agreement.
    /// </summary>
    public Schedule? ScheduleFor(
        Guid employeeId, DateOnly date, Guid? ownScheduleId, IReadOnlyDictionary<Guid, Schedule> schedules)
        => EffectiveScheduleId(employeeId, date, ownScheduleId) is Guid id
            ? schedules.GetValueOrDefault(id)
            : null;
}
