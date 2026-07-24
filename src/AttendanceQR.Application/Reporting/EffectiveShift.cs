using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// The hours and working days that actually apply to one employee, after the three possible sources
/// have been resolved against each other.
///
/// This exists because the decision used to be made in five places — the scan endpoint, the nightly
/// summary job, the live board, the tabel and the reminder job — each spelling out
/// <c>employee.WorkStart ?? location.ShiftStart</c> for itself. Five copies of a rule is five chances
/// for a day to be judged one way at scan time and another in the report, which is exactly the class
/// of bug that made a night worker vanish from the board at midnight.
///
/// Precedence, and it is the same for hours, working days and rotation:
///
///   1. the employee's assigned <see cref="Schedule"/> ("növbə"), if they are on one
///   2. otherwise their own WorkStart/WorkEnd and rotation fields
///   3. otherwise the location's
///
/// A shift wins outright rather than merging field by field. Half a shift plus half a personal
/// override is not something anyone could read off a screen and predict.
/// </summary>
public readonly record struct EffectiveShift(
    TimeOnly Start,
    TimeOnly End,
    int LateThresholdMinutes,
    int WorkDaysMask,
    int? CycleDays,
    int CycleOnDays,
    DateOnly? CycleAnchor,
    /// <summary>Name of the schedule this came from, or null when it came from the employee or the
    /// location. For display only — screens show "Gecə A" rather than a pair of times.</summary>
    string? ScheduleName)
{
    /// <summary>True when this shift crosses midnight (22:00–06:00), the convention used everywhere.</summary>
    public bool IsOvernight => End < Start;

    /// <summary>
    /// Whether the shift is scheduled to work on <paramref name="date"/>, before company holidays are
    /// taken into account — callers still subtract <c>NonWorkingDay</c> themselves.
    /// </summary>
    public bool IsWorkingDay(DateOnly date)
        => AttendanceCalculator.IsScheduledWorkingDay(CycleDays, CycleOnDays, CycleAnchor, WorkDaysMask, date);

    /// <summary>
    /// Resolves the shift for an employee. <paramref name="schedule"/> is their assigned one, or null
    /// — callers usually hold a dictionary of the tenant's schedules and look it up by
    /// <see cref="Employee.ScheduleId"/>, since there are only ever a handful of them.
    /// </summary>
    public static EffectiveShift Resolve(Employee employee, Schedule? schedule, Location location)
        => Resolve(
            employee.WorkStart, employee.WorkEnd,
            employee.WorkCycleDays, employee.WorkCycleOnDays, employee.WorkCycleAnchor,
            schedule, location);

    /// <summary>
    /// The same rule, taking the employee's fields loose — most callers read employees through a
    /// narrow projection rather than loading the whole entity.
    /// </summary>
    public static EffectiveShift Resolve(
        TimeOnly? employeeStart, TimeOnly? employeeEnd,
        int? employeeCycleDays, int employeeCycleOnDays, DateOnly? employeeCycleAnchor,
        Schedule? schedule, Location location)
    {
        if (schedule is not null)
            return new EffectiveShift(
                schedule.ShiftStart, schedule.ShiftEnd, schedule.LateThresholdMinutes,
                schedule.WorkDaysMask, schedule.WorkCycleDays, schedule.WorkCycleOnDays,
                schedule.WorkCycleAnchor, schedule.Name);

        return new EffectiveShift(
            employeeStart ?? location.ShiftStart,
            employeeEnd ?? location.ShiftEnd,
            location.LateThresholdMinutes,
            location.WorkDaysMask,
            employeeCycleDays, employeeCycleOnDays, employeeCycleAnchor,
            ScheduleName: null);
    }
}
