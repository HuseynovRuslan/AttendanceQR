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
    /// Resolves the status to report when an employee has no check-in for the date, in priority
    /// order: an approved LeaveRecord beats everything (even a non-working day — being on
    /// vacation over a weekend still reads as leave, not "day off"), then DayOff on a non-working
    /// day, then plain Absent. Shared by DailySummaryService and ReportQueryService so the two
    /// never resolve this differently.
    /// </summary>
    public static DailySummaryStatus ResolveNoRecordStatus(bool isWorkingDay, LeaveType? leaveType)
    {
        if (leaveType is not null)
            return leaveType == LeaveType.Permission ? DailySummaryStatus.Permission : DailySummaryStatus.OnLeave;
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

        var minutesAfterStart =
            (TimeOnly.FromDateTime(localCheckIn).ToTimeSpan() - shiftStart.ToTimeSpan()).TotalMinutes;

        var workedMinutes = (int)Math.Round((record.CheckOutAtUtc.Value - record.CheckInAtUtc.Value).TotalMinutes);

        // A non-working day has no concept of "late" — showing up at all is a bonus, not tardy.
        DailySummaryStatus status;
        var lateMinutes = 0;
        if (isWorkingDay && minutesAfterStart > location.LateThresholdMinutes)
        {
            status = DailySummaryStatus.Late;
            lateMinutes = (int)Math.Round(minutesAfterStart);
        }
        else
        {
            status = DailySummaryStatus.OnTime;
        }

        var overtime = (TimeOnly.FromDateTime(localCheckOut).ToTimeSpan() - shiftEnd.ToTimeSpan()).TotalMinutes;
        var overtimeMinutes = overtime > 0 ? (int)Math.Round(overtime) : 0;

        return new DayComputation(status, workedMinutes, lateMinutes, overtimeMinutes);
    }
}
