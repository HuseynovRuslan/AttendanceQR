using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Covers <see cref="AttendanceCalculator.Compute"/> — the one place that turns a raw record + shift
/// into a status/minutes. Both real bugs this code has shipped are pinned here:
///   • UTC-vs-local: shift times are Asia/Baku wall-clock, the record is UTC (a 19:00-local check-out
///     must not read as "before 18:00").
///   • Per-employee hours: Employee.WorkStart/WorkEnd must override the location shift.
/// </summary>
public class AttendanceCalculatorTests
{
    // The app's real timezone (AppOptions default). UTC+4, no DST since 2016.
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private static Location Loc(string shiftStart = "09:00", string shiftEnd = "18:00", int lateThreshold = 15) =>
        new()
        {
            ShiftStart = TimeOnly.Parse(shiftStart),
            ShiftEnd = TimeOnly.Parse(shiftEnd),
            LateThresholdMinutes = lateThreshold,
            WorkDaysMask = 126,
        };

    /// <summary>A record whose check-in/out are given as BAKU LOCAL times, stored as the UTC instants
    /// the app would really persist (local − 4h).</summary>
    private static AttendanceRecord Record(string localCheckIn, string? localCheckOut = null)
    {
        static DateTime ToUtc(string local) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(DateTime.Parse(local), DateTimeKind.Unspecified), Baku);

        return new AttendanceRecord
        {
            CheckInAtUtc = ToUtc(localCheckIn),
            CheckOutAtUtc = localCheckOut is null ? null : ToUtc(localCheckOut),
        };
    }

    /// <summary>Resolves the shift exactly as production does — no schedule assigned, so the
    /// employee's own hours win over the location's when given.</summary>
    private static DayComputation Run(
        AttendanceRecord? record, Location location, TimeOnly? workStart = null, TimeOnly? workEnd = null,
        bool isWorkingDay = true) =>
        AttendanceCalculator.Compute(
            record,
            EffectiveShift.Resolve(workStart, workEnd, null, 1, null, null, location),
            Baku, isWorkingDay, DailySummaryStatus.Absent);

    // --- overnight / night shift (22:00–06:00, crosses midnight) --------------------------------

    private static Location Night() => Loc("22:00", "06:00", lateThreshold: 15);

    [Fact]
    public void Night_on_time_check_in_just_after_start()
    {
        // 22:05 in, 06:00 out next day — 5 min after a 22:00 start, inside the 15-min grace.
        var c = Run(Record("2026-07-15 22:05", "2026-07-16 06:00"), Night());
        Assert.Equal(DailySummaryStatus.OnTime, c.Status);
        Assert.Equal(0, c.LateMinutes);
    }

    [Fact]
    public void Night_check_in_after_midnight_is_late_across_the_boundary()
    {
        // Arrived 00:30 for a 22:00 shift — 2.5 h late. The naive time-of-day math read this as
        // wildly early (00:30 < 22:00); the noon pivot puts 00:30 on the next day, so it is +150.
        var c = Run(Record("2026-07-16 00:30", "2026-07-16 06:00"), Night());
        Assert.Equal(DailySummaryStatus.Late, c.Status);
        Assert.Equal(150, c.LateMinutes);
    }

    [Fact]
    public void Night_arriving_before_start_is_not_late()
    {
        // 21:50 for a 22:00 shift — 10 min early, must NOT be treated as ~24 h late.
        var c = Run(Record("2026-07-15 21:50", "2026-07-16 06:00"), Night());
        Assert.Equal(DailySummaryStatus.OnTime, c.Status);
        Assert.Equal(0, c.LateMinutes);
    }

    [Fact]
    public void Night_checkout_after_shift_end_is_overtime()
    {
        // Left 06:30, shift ends 06:00 next morning → 30 min overtime, across midnight.
        var c = Run(Record("2026-07-15 22:00", "2026-07-16 06:30"), Night());
        Assert.Equal(30, c.OvertimeMinutes);
    }

    [Fact]
    public void Night_checkout_before_shift_end_has_no_overtime()
    {
        var c = Run(Record("2026-07-15 22:00", "2026-07-16 05:30"), Night());
        Assert.Equal(0, c.OvertimeMinutes);
    }

    [Fact]
    public void Night_worked_minutes_span_midnight()
    {
        // 22:00 → 06:00 is a full 8 hours even though it crosses midnight (absolute span).
        var c = Run(Record("2026-07-15 22:00", "2026-07-16 06:00"), Night());
        Assert.Equal(480, c.WorkedMinutes);
    }

    [Fact]
    public void Night_per_employee_hours_can_also_be_overnight()
    {
        // The employee's own hours override the location shift, overnight included.
        var c = Run(
            Record("2026-07-15 23:10", "2026-07-16 07:00"), Loc("09:00", "18:00"),
            workStart: TimeOnly.Parse("23:00"), workEnd: TimeOnly.Parse("07:00"));
        Assert.Equal(DailySummaryStatus.OnTime, c.Status); // 10 min after 23:00, within grace
        Assert.Equal(0, c.OvertimeMinutes);
    }

    // --- timezone (the bug that asked Ənvər why he left early at 19:00) --------------------------

    [Fact]
    public void CheckIn_before_shift_start_is_OnTime_in_local_time()
    {
        // 08:45 local = 04:45Z. Comparing the raw UTC 04:45 against 09:00 would also say "on time"
        // here — the point is it must stay OnTime once the comparison is done in local time.
        var c = Run(Record("2026-07-15 08:45", "2026-07-15 18:00"), Loc());
        Assert.Equal(DailySummaryStatus.OnTime, c.Status);
        Assert.Equal(0, c.LateMinutes);
    }

    [Fact]
    public void CheckIn_after_threshold_is_Late_with_local_minutes()
    {
        // 09:56 local (= 05:56Z) vs 09:00 + 15 min grace → late by 56 minutes.
        // The UTC bug made this read 05:56 vs 09:15 → "on time", so lateness never fired at all.
        var c = Run(Record("2026-07-15 09:56", "2026-07-15 18:00"), Loc());
        Assert.Equal(DailySummaryStatus.Late, c.Status);
        Assert.Equal(56, c.LateMinutes);
    }

    [Fact]
    public void CheckIn_inside_grace_is_OnTime()
    {
        var c = Run(Record("2026-07-15 09:10", "2026-07-15 18:00"), Loc()); // 10 min < 15 grace
        Assert.Equal(DailySummaryStatus.OnTime, c.Status);
    }

    [Fact]
    public void CheckOut_after_shift_end_is_overtime_not_early()
    {
        // 19:00 local = 15:00Z. The UTC bug compared 15:00 < 18:00 → wrongly "early"; in local time
        // it is an hour of OVERTIME.
        var c = Run(Record("2026-07-15 09:00", "2026-07-15 19:00"), Loc());
        Assert.Equal(60, c.OvertimeMinutes);
    }

    [Fact]
    public void CheckOut_before_shift_end_has_no_overtime()
    {
        var c = Run(Record("2026-07-15 09:00", "2026-07-15 17:00"), Loc());
        Assert.Equal(0, c.OvertimeMinutes);
    }

    // --- per-employee hours (the bug fixed today) -----------------------------------------------

    [Fact]
    public void Employee_work_start_overrides_location_shift()
    {
        // Location opens 09:00, but this employee's own shift starts 11:00. Arriving 10:30 local is
        // EARLY for them — against the location shift alone it would wrongly be 90 min late.
        var c = Run(Record("2026-07-15 10:30", "2026-07-15 18:00"), Loc(), workStart: TimeOnly.Parse("11:00"));
        Assert.Equal(DailySummaryStatus.OnTime, c.Status);
        Assert.Equal(0, c.LateMinutes);
    }

    [Fact]
    public void Employee_work_end_overrides_location_shift_for_overtime()
    {
        // Employee's day ends 16:00, so leaving 18:00 local is 2 h overtime (not 0 as the location's
        // 18:00 shift would say).
        var c = Run(Record("2026-07-15 09:00", "2026-07-15 18:00"), Loc(), workEnd: TimeOnly.Parse("16:00"));
        Assert.Equal(120, c.OvertimeMinutes);
    }

    [Fact]
    public void Null_employee_hours_fall_back_to_location_shift()
    {
        // The default for everyone today (nobody has custom hours) — must behave exactly as before.
        var withNulls = Run(Record("2026-07-15 09:56", "2026-07-15 18:00"), Loc());
        var explicitSame = Run(
            Record("2026-07-15 09:56", "2026-07-15 18:00"), Loc(),
            workStart: TimeOnly.Parse("09:00"), workEnd: TimeOnly.Parse("18:00"));
        Assert.Equal(explicitSame.Status, withNulls.Status);
        Assert.Equal(explicitSame.LateMinutes, withNulls.LateMinutes);
        Assert.Equal(explicitSame.OvertimeMinutes, withNulls.OvertimeMinutes);
    }

    // --- worked minutes / statuses ---------------------------------------------------------------

    [Fact]
    public void Worked_minutes_span_check_in_to_check_out()
    {
        var c = Run(Record("2026-07-15 09:00", "2026-07-15 17:30"), Loc());
        Assert.Equal(510, c.WorkedMinutes); // 8 h 30
    }

    [Fact]
    public void Check_in_without_check_out_is_Incomplete_and_zero_minutes()
    {
        var c = Run(Record("2026-07-15 09:00"), Loc());
        Assert.Equal(DailySummaryStatus.Incomplete, c.Status);
        Assert.Equal(0, c.WorkedMinutes);
    }

    [Fact]
    public void No_record_uses_the_caller_supplied_status()
    {
        var c = Run(null, Loc());
        Assert.Equal(DailySummaryStatus.Absent, c.Status);
    }

    [Fact]
    public void Non_working_day_is_never_Late()
    {
        // Turning up at 11:00 on a day off is a bonus, not tardiness.
        var c = Run(Record("2026-07-15 11:00", "2026-07-15 15:00"), Loc(), isWorkingDay: false);
        Assert.Equal(DailySummaryStatus.OnTime, c.Status);
        Assert.Equal(0, c.LateMinutes);
    }

    // --- working-day mask / no-record status -----------------------------------------------------

    [Theory]
    [InlineData(126, DayOfWeek.Monday, true)]   // 126 = every day except Sunday
    [InlineData(126, DayOfWeek.Sunday, false)]
    [InlineData(127, DayOfWeek.Sunday, true)]   // 127 = all seven
    public void IsWorkingDayOfWeek_reads_the_mask_bit(int mask, DayOfWeek day, bool expected)
        => Assert.Equal(expected, AttendanceCalculator.IsWorkingDayOfWeek(mask, day));

    [Theory]
    [InlineData(true, null, DailySummaryStatus.Absent)]
    [InlineData(false, null, DailySummaryStatus.DayOff)]
    [InlineData(true, LeaveType.Vacation, DailySummaryStatus.OnLeave)]
    [InlineData(true, LeaveType.Sick, DailySummaryStatus.OnLeave)]
    [InlineData(true, LeaveType.Permission, DailySummaryStatus.Permission)]
    // Leave beats a non-working day: being on holiday over a weekend still reads as leave.
    [InlineData(false, LeaveType.Vacation, DailySummaryStatus.OnLeave)]
    public void ResolveNoRecordStatus_priority(bool isWorkingDay, LeaveType? leave, DailySummaryStatus expected)
        => Assert.Equal(expected, AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leave));
}
