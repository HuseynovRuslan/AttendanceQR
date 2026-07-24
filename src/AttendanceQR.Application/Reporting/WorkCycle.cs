using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Writing a rotation onto an employee. The reading half — deciding whether a given date falls on a
/// working day — lives in <see cref="AttendanceCalculator.IsScheduledWorkingDay"/>.
///
/// Shared by the admin and manager edit paths so the two can never disagree about what a valid
/// rotation is; a manager is usually the one who actually knows who works which half of the cycle.
/// </summary>
public static class WorkCycle
{
    /// <summary>Longest cycle we accept. Beyond a month it stops being a rotation someone can verify
    /// by eye and becomes a roster, which is a different feature.</summary>
    public const int MaxCycleDays = 28;

    /// <summary>
    /// Applies a rotation, or clears it when <paramref name="days"/> is null. Returns an error code,
    /// or null on success.
    ///
    /// Validated rather than quietly clamped: a half-set rotation is worse than no rotation at all. A
    /// cycle with no anchor has no day 0, so every single day would resolve to Absent — and an
    /// unexcused absence comes straight off that person's salary in the payroll report. Either all
    /// three values are coherent or the employee keeps the location's weekly calendar.
    /// </summary>
    public static string? Apply(Employee employee, int? days, int? onDays, DateOnly? anchor)
    {
        if (days is null)
        {
            // Clear the other two as well, so re-enabling a rotation months later cannot inherit a
            // stale anchor and land the person on the wrong half of the cycle.
            employee.WorkCycleDays = null;
            employee.WorkCycleOnDays = 1;
            employee.WorkCycleAnchor = null;
            return null;
        }

        if (days is < 2 or > MaxCycleDays) return "WorkCycleDaysInvalid";
        var on = onDays ?? 1;
        // A cycle that is worked end to end is not a rotation — it would just be "every day", and
        // saying so via a cycle would hide the fact from every screen that reads the weekly calendar.
        if (on < 1 || on >= days) return "WorkCycleOnDaysInvalid";
        if (anchor is null) return "WorkCycleAnchorRequired";

        employee.WorkCycleDays = days;
        employee.WorkCycleOnDays = on;
        employee.WorkCycleAnchor = anchor;
        return null;
    }
}
