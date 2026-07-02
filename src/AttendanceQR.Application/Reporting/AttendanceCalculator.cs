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
    public static DayComputation Compute(AttendanceRecord? record, Location location, TimeZoneInfo timeZone)
    {
        // No record → the employee never showed up.
        if (record is null || record.CheckInAtUtc is null)
            return new DayComputation(DailySummaryStatus.Absent, 0, 0, 0);

        // Checked in but never out.
        if (record.CheckOutAtUtc is null)
            return new DayComputation(DailySummaryStatus.Incomplete, 0, 0, 0);

        // Timezone: CheckInAtUtc is a UTC instant; ShiftStart/ShiftEnd are LOCAL wall-clock times.
        // Convert to local (Asia/Baku = UTC+4) before comparing, otherwise a 05:45Z check-in would
        // look like it beat a 09:00 shift when it is really 09:45 local (45 min late).
        var localCheckIn = TimeZoneInfo.ConvertTimeFromUtc(record.CheckInAtUtc.Value, timeZone);
        var localCheckOut = TimeZoneInfo.ConvertTimeFromUtc(record.CheckOutAtUtc.Value, timeZone);

        var minutesAfterStart =
            (TimeOnly.FromDateTime(localCheckIn).ToTimeSpan() - location.ShiftStart.ToTimeSpan()).TotalMinutes;

        var workedMinutes = (int)Math.Round((record.CheckOutAtUtc.Value - record.CheckInAtUtc.Value).TotalMinutes);

        DailySummaryStatus status;
        var lateMinutes = 0;
        if (minutesAfterStart > location.LateThresholdMinutes)
        {
            status = DailySummaryStatus.Late;
            lateMinutes = (int)Math.Round(minutesAfterStart);
        }
        else
        {
            status = DailySummaryStatus.OnTime;
        }

        var overtime = (TimeOnly.FromDateTime(localCheckOut).ToTimeSpan() - location.ShiftEnd.ToTimeSpan()).TotalMinutes;
        var overtimeMinutes = overtime > 0 ? (int)Math.Round(overtime) : 0;

        return new DayComputation(status, workedMinutes, lateMinutes, overtimeMinutes);
    }
}
