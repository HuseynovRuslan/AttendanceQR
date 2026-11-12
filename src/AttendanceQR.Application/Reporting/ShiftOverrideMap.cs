using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Which schedule answers for one person on one date: the day's own plan («əvəzləmə», a planned shift, a planned
/// day off), else the standing shift that covered that date (<see cref="ShiftAssignment"/> — one shift or a
/// rotation), else the person's <see cref="Employee.ScheduleId"/> for somebody whose standing shift was never
/// changed since assignments came in.
///
/// It is a lookup and nothing else, and that is the point. It does not introduce a new kind of shift or a new
/// branch in the day's arithmetic — it only changes WHICH <c>Schedule</c> the existing resolver is handed. Everything
/// downstream then applies unchanged: the overnight noon-pivot, the working-day mask, the late threshold, per-day
/// hours.
///
/// Loaded once per query (<see cref="LoadAsync"/>) rather than looked up per row — the reporting screens walk
/// hundreds of employee-days and a per-row round trip is how a month's tabel becomes a minute.
/// </summary>
public sealed class ShiftOverrideMap
{
    /// <summary>No overrides at all — what every caller uses until it loads some.</summary>
    public static readonly ShiftOverrideMap Empty = new(new Dictionary<(Guid, DateOnly), Guid>());

    private readonly Dictionary<(Guid EmployeeId, DateOnly Date), Guid> _byEmployeeDay;
    private readonly Dictionary<Guid, ShiftAssignment[]> _standing;

    public ShiftOverrideMap(Dictionary<(Guid, DateOnly), Guid> byEmployeeDay, IEnumerable<ShiftAssignment>? standing = null)
    {
        _byEmployeeDay = byEmployeeDay;
        _standing = (standing ?? [])
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.From).ToArray());
    }

    /// <summary>
    /// The plan rows and standing shifts for <paramref name="from"/>–<paramref name="to"/>, for
    /// <paramref name="employees"/> or everybody. The one way the readers load it, so none of them forgets the
    /// standing shifts.
    /// </summary>
    public static async Task<ShiftOverrideMap> LoadAsync(
        AppDbContext db, DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? employees, CancellationToken ct)
    {
        var days = db.ShiftOverrides.AsNoTracking().Where(o => o.Date >= from && o.Date <= to);
        var standing = db.ShiftAssignments.AsNoTracking().Where(a => a.From <= to && (a.Until == null || a.Until >= from));
        if (employees is not null)
        {
            days = days.Where(o => employees.Contains(o.EmployeeId));
            standing = standing.Where(a => employees.Contains(a.EmployeeId));
        }
        return new ShiftOverrideMap(
            await days.ToDictionaryAsync(o => (o.EmployeeId, o.Date), o => o.ScheduleId, ct),
            await standing.ToListAsync(ct));
    }

    public bool IsEmpty => _byEmployeeDay.Count == 0 && _standing.Count == 0;

    /// <summary>The schedule planned for this person on this date (a shift or the rest day), or null.</summary>
    public Guid? On(Guid employeeId, DateOnly date)
        => _byEmployeeDay.TryGetValue((employeeId, date), out var id) ? id : null;

    /// <summary>The standing shift that covered this person on this date, or null.</summary>
    public ShiftAssignment? StandingOn(Guid employeeId, DateOnly date)
        => _standing.TryGetValue(employeeId, out var list) ? list.LastOrDefault(a => a.Covers(date)) : null;

    /// <summary>Whether the person's standing shifts are recorded as assignments at all (else <c>ScheduleId</c>).</summary>
    public bool HasStanding(Guid employeeId) => _standing.ContainsKey(employeeId);

    /// <summary>
    /// The schedule id the day is resolved with, before rest days and history: the plan's, the standing
    /// rotation's step, or the person's own.
    /// </summary>
    public Guid? EffectiveScheduleId(Guid employeeId, DateOnly date, Guid? ownScheduleId)
    {
        if (On(employeeId, date) is Guid planned)
            return planned;
        if (StandingOn(employeeId, date) is { } standing)
            return Guid.TryParse(standing.StepOn(date), out var step) ? step : standing.FirstShift();
        return ownScheduleId;
    }

    /// <summary>
    /// The schedule object itself — through <see cref="ShiftResolver"/>: a rest day keeps the hours of the shift the
    /// person stands on, and a shift answers as it was on that date. Every call site is one line, which is the only
    /// way the readers stay in agreement.
    /// </summary>
    public Schedule? ScheduleFor(
        Guid employeeId, DateOnly date, Guid? ownScheduleId, IReadOnlyDictionary<Guid, Schedule> schedules)
    {
        var standing = StandingOn(employeeId, date);
        // Whose hours a rest day keeps: the standing shift's, or the person's own.
        var hours = standing is not null ? standing.FirstShift() : ownScheduleId;

        if (On(employeeId, date) is Guid planned)
            return ShiftResolver.ScheduleOn(planned, hours, date, schedules);

        if (standing is not null)
        {
            var step = standing.StepOn(date);
            if (step == ShiftAssignment.Rest)
                return ShiftResolver.RestOn(hours, date, schedules);
            // An empty step: the person's own hours or their branch's — no named shift.
            return Guid.TryParse(step, out var id) ? ShiftResolver.ScheduleOn(id, hours, date, schedules) : null;
        }

        return ShiftResolver.ScheduleOn(ownScheduleId, ownScheduleId, date, schedules);
    }
}
