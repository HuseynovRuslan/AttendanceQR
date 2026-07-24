using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Application.Reporting;

/// <summary>Computed attendance figures for one employee on one day.</summary>
public readonly record struct DayComputation(
    DailySummaryStatus Status,
    int WorkedMinutes,
    int LateMinutes,
    int OvertimeMinutes);

/// <summary>
/// The single place that turns a raw <see cref="AttendanceRecord"/> + a location's shift + the app
/// time zone into a status and the minute figures. Shared by the nightly <c>DailySummaryService</c>
/// (persisted) and the live "today" query (on the fly), so the timezone/late logic exists once.
/// </summary>
public static class AttendanceCalculator
{
    /// <summary>
    /// True when <paramref name="dayOfWeek"/> is a working day per the location's WorkDaysMask
    /// bitmask (bit index = System.DayOfWeek, so Sunday=0 ... Saturday=6).
    /// </summary>
    public static bool IsWorkingDayOfWeek(int workDaysMask, DayOfWeek dayOfWeek)
        => (workDaysMask & (1 << (int)dayOfWeek)) != 0;

    /// <summary>
    /// Whether <paramref name="date"/> is a scheduled working day for this employee, before holidays
    /// are considered.
    ///
    /// An employee on a rotation (<see cref="Employee.WorkCycleDays"/>) ignores the location's weekly
    /// mask entirely — a cycle whose length isn't 7 cannot be expressed as one, so mixing them would
    /// silently drop half the rotation's days. Everyone else falls through to the mask, which is what
    /// every employee did before rotations existed.
    ///
    /// The cycle is anchored to <see cref="Employee.WorkCycleAnchor"/>: that date is day 0 of the
    /// cycle, and the first <see cref="Employee.WorkCycleOnDays"/> days of each repeat are worked.
    /// An incompletely configured rotation (no anchor, or nonsense values an older row could hold)
    /// falls back to the mask rather than marking someone absent on every day of their life.
    /// </summary>
    /// <remarks>
    /// Takes the three cycle values loose rather than an <see cref="Employee"/> because most callers
    /// read employees through a narrow projection, not the whole entity.
    /// </remarks>
    public static bool IsScheduledWorkingDay(
        int? cycleDays, int cycleOnDays, DateOnly? cycleAnchor, int workDaysMask, DateOnly date)
    {
        if (cycleDays is null or < 2 || cycleAnchor is null)
            return IsWorkingDayOfWeek(workDaysMask, date.DayOfWeek);

        var onDays = Math.Clamp(cycleOnDays, 1, cycleDays.Value - 1);

        // DayNumber difference, floored into [0, cycle) so dates BEFORE the anchor land on the right
        // day of the cycle too — C#'s % keeps the sign of the dividend, which would put them outside.
        var offset = date.DayNumber - cycleAnchor.Value.DayNumber;
        var dayInCycle = ((offset % cycleDays.Value) + cycleDays.Value) % cycleDays.Value;

        return dayInCycle < onDays;
    }

    /// <inheritdoc cref="IsScheduledWorkingDay(int?, int, DateOnly?, int, DateOnly)"/>
    public static bool IsScheduledWorkingDay(Employee employee, int workDaysMask, DateOnly date)
        => IsScheduledWorkingDay(
            employee.WorkCycleDays, employee.WorkCycleOnDays, employee.WorkCycleAnchor, workDaysMask, date);

    /// <summary>
    /// Resolves the status to report when an employee has no check-in for the date, in priority
    /// order: an approved LeaveRecord beats everything (even a non-working day — being on
    /// vacation over a weekend still reads as leave, not "day off"), then DayOff on a non-working
    /// day, then plain Absent. Shared by DailySummaryService and ReportQueryService so the two
    /// never resolve this differently.
    /// </summary>
    public static DailySummaryStatus ResolveNoRecordStatus(bool isWorkingDay, LeaveType? leaveType)
    {
        if (leaveType is not null)
            return leaveType switch
            {
                // A rest day is a day off, not planned leave — it shows as İstirahət and, unlike
                // Absent, never costs the employee pay.
                LeaveType.Rest => DailySummaryStatus.DayOff,
                LeaveType.Permission => DailySummaryStatus.Permission,
                _ => DailySummaryStatus.OnLeave,
            };
        return isWorkingDay ? DailySummaryStatus.Absent : DailySummaryStatus.DayOff;
    }

    /// <param name="isWorkingDay">
    /// Whether this date is a working day for this location — the location's weekly WorkDaysMask
    /// AND no admin-declared NonWorkingDay for this date/location. Callers compute this (it needs a
    /// NonWorkingDay lookup this method doesn't have access to).
    /// </param>
    /// <param name="noRecordStatus">
    /// Status to report when nobody checked in — Absent on a working day, DayOff otherwise (or,
    /// once leave/permission exists, OnLeave/Permission — the caller decides).
    /// </param>
    /// <param name="employeeWorkStart">
    /// The employee's own <see cref="Employee.WorkStart"/>, when set — it overrides the location's
    /// ShiftStart for THIS employee (staff at one location can keep different hours). Null → the
    /// location's shift. Must mirror AttendanceController.EffectiveShiftStart, or a day would read
    /// one way at scan time and another in the report.
    /// </param>
    /// <param name="employeeWorkEnd">Same as employeeWorkStart, for the shift end (overtime).</param>
    public static DayComputation Compute(
        AttendanceRecord? record, Location location, TimeZoneInfo timeZone,
        bool isWorkingDay, DailySummaryStatus noRecordStatus,
        TimeOnly? employeeWorkStart = null, TimeOnly? employeeWorkEnd = null)
    {
        var shiftStart = employeeWorkStart ?? location.ShiftStart;
        var shiftEnd = employeeWorkEnd ?? location.ShiftEnd;

        // No record → the employee never showed up (or it wasn't a working day / they were on
        // leave — whichever the caller determined via noRecordStatus).
        if (record is null || record.CheckInAtUtc is null)
            return new DayComputation(noRecordStatus, 0, 0, 0);

        // Checked in but never out.
        if (record.CheckOutAtUtc is null)
            return new DayComputation(DailySummaryStatus.Incomplete, 0, 0, 0);

        // Timezone: CheckInAtUtc is a UTC instant; ShiftStart/ShiftEnd are LOCAL wall-clock times.
        // Convert to local (Asia/Baku = UTC+4) before comparing, otherwise a 05:45Z check-in would
        // look like it beat a 09:00 shift when it is really 09:45 local (45 min late).
        var localCheckIn = TimeZoneInfo.ConvertTimeFromUtc(record.CheckInAtUtc.Value, timeZone);
        var localCheckOut = TimeZoneInfo.ConvertTimeFromUtc(record.CheckOutAtUtc.Value, timeZone);

        // An overnight shift (e.g. 22:00–06:00) is one whose end is EARLIER than its start — it
        // crosses midnight. Put every time-of-day on a single continuous timeline with a NOON PIVOT:
        // for overnight shifts, anything before noon is the *next* day, so 06:00 comes after 22:00.
        // Day shifts leave every value untouched, so their late/overtime is computed exactly as before.
        var overnight = shiftEnd < shiftStart;
        int OnTimeline(TimeOnly t)
        {
            var m = (int)t.ToTimeSpan().TotalMinutes;
            return overnight && m < 12 * 60 ? m + 24 * 60 : m;
        }

        var startMin = OnTimeline(shiftStart);
        var endMin = OnTimeline(shiftEnd);
        var checkInMin = OnTimeline(TimeOnly.FromDateTime(localCheckIn));
        var checkOutMin = OnTimeline(TimeOnly.FromDateTime(localCheckOut));

        var minutesAfterStart = checkInMin - startMin;

        var workedMinutes = (int)Math.Round((record.CheckOutAtUtc.Value - record.CheckInAtUtc.Value).TotalMinutes);

        // A non-working day has no concept of "late" — showing up at all is a bonus, not tardy.
        DailySummaryStatus status;
        var lateMinutes = 0;
        if (isWorkingDay && minutesAfterStart > location.LateThresholdMinutes)
        {
            status = DailySummaryStatus.Late;
            lateMinutes = minutesAfterStart;
        }
        else
        {
            status = DailySummaryStatus.OnTime;
        }

        var overtime = checkOutMin - endMin;
        var overtimeMinutes = overtime > 0 ? overtime : 0;

        return new DayComputation(status, workedMinutes, lateMinutes, overtimeMinutes);
    }
}
