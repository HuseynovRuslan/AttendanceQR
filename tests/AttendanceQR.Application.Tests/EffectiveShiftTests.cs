using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The precedence rule: assigned shift → the employee's own hours → the location's.
///
/// This used to be spelled out separately in the scan endpoint, the nightly job, the live board, the
/// tabel and the reminder job. Pinning it here is the point of having collapsed it into one place: if
/// the order ever changes, a day would be judged one way at scan time and another in the report,
/// which is how a night worker once disappeared from the board at midnight.
/// </summary>
public class EffectiveShiftTests
{
    private static Location Loc(
        string start = "09:00", string end = "18:00", int late = 15, int mask = 126) => new()
    {
        Id = Guid.NewGuid(),
        ShiftStart = TimeOnly.Parse(start),
        ShiftEnd = TimeOnly.Parse(end),
        LateThresholdMinutes = late,
        WorkDaysMask = mask,
    };

    private static Schedule Shift(
        string name = "Gecə A", string start = "21:00", string end = "07:00", int late = 10,
        int mask = 126, int? cycle = null, int onDays = 1, string? anchor = null) => new()
    {
        Name = name,
        ShiftStart = TimeOnly.Parse(start),
        ShiftEnd = TimeOnly.Parse(end),
        LateThresholdMinutes = late,
        WorkDaysMask = mask,
        WorkCycleDays = cycle,
        WorkCycleOnDays = onDays,
        WorkCycleAnchor = anchor is null ? null : DateOnly.Parse(anchor),
    };

    private static Employee Emp(
        string? start = null, string? end = null, int? cycle = null, int onDays = 1, string? anchor = null) => new()
    {
        WorkStart = start is null ? null : TimeOnly.Parse(start),
        WorkEnd = end is null ? null : TimeOnly.Parse(end),
        WorkCycleDays = cycle,
        WorkCycleOnDays = onDays,
        WorkCycleAnchor = anchor is null ? null : DateOnly.Parse(anchor),
    };

    [Fact]
    public void NoShiftNoOverride_UsesTheLocation()
    {
        var s = EffectiveShift.Resolve(Emp(), null, Loc("08:30", "17:30", late: 20));

        Assert.Equal(new TimeOnly(8, 30), s.Start);
        Assert.Equal(new TimeOnly(17, 30), s.End);
        Assert.Equal(20, s.LateThresholdMinutes);
        Assert.Null(s.ScheduleName);
    }

    [Fact]
    public void EmployeeHours_BeatTheLocation()
    {
        var s = EffectiveShift.Resolve(Emp("21:00", "07:00"), null, Loc("09:00", "18:00"));

        Assert.Equal(new TimeOnly(21, 0), s.Start);
        Assert.Equal(new TimeOnly(7, 0), s.End);
        Assert.True(s.IsOvernight);
    }

    [Fact]
    public void AssignedShift_BeatsBoth()
    {
        // The employee also carries their own hours — a leftover from before they were assigned a
        // shift. The shift has to win outright, or the row shows "Gecə A" while being judged by
        // hours nobody can see on the screen.
        var s = EffectiveShift.Resolve(
            Emp("12:00", "20:00"), Shift(start: "21:00", end: "07:00", late: 10), Loc("09:00", "18:00"));

        Assert.Equal(new TimeOnly(21, 0), s.Start);
        Assert.Equal(new TimeOnly(7, 0), s.End);
        Assert.Equal(10, s.LateThresholdMinutes);
        Assert.Equal("Gecə A", s.ScheduleName);
    }

    [Fact]
    public void AssignedShift_TakesItsRotationTooNotTheEmployeesOwn()
    {
        // 2026-07-01 is a Wednesday. The shift works every other day from the 1st; the employee's own
        // stale rotation is anchored a day later and must be ignored entirely, not merged.
        var s = EffectiveShift.Resolve(
            Emp(cycle: 2, anchor: "2026-07-02"),
            Shift(cycle: 2, onDays: 1, anchor: "2026-07-01"),
            Loc());

        Assert.True(s.IsWorkingDay(new DateOnly(2026, 7, 1)));
        Assert.False(s.IsWorkingDay(new DateOnly(2026, 7, 2)));
        Assert.True(s.IsWorkingDay(new DateOnly(2026, 7, 3)));
    }

    [Fact]
    public void TwoCrewsOnTheSameRotationAreTwoShiftsAnchoredADayApart()
    {
        // The reason the anchor lives on the shift rather than on each person: "Gecə A" and "Gecə B"
        // cover every day between them, and assigning someone is one choice, not a choice plus a date.
        var a = EffectiveShift.Resolve(Emp(), Shift("Gecə A", cycle: 2, anchor: "2026-07-01"), Loc());
        var b = EffectiveShift.Resolve(Emp(), Shift("Gecə B", cycle: 2, anchor: "2026-07-02"), Loc());

        foreach (var day in Enumerable.Range(1, 14).Select(d => new DateOnly(2026, 7, d)))
            Assert.True(a.IsWorkingDay(day) ^ b.IsWorkingDay(day), $"{day} should be covered by exactly one crew");
    }

    [Fact]
    public void ShiftWithoutRotation_UsesItsOwnWeeklyDaysNotTheLocations()
    {
        // Location works Mon–Sat (126); the shift is Mon/Wed/Fri only (bits 1,3,5 = 42).
        var s = EffectiveShift.Resolve(Emp(), Shift(mask: 42), Loc(mask: 126));

        Assert.True(s.IsWorkingDay(new DateOnly(2026, 7, 6)));   // Monday
        Assert.False(s.IsWorkingDay(new DateOnly(2026, 7, 7)));  // Tuesday
        Assert.True(s.IsWorkingDay(new DateOnly(2026, 7, 8)));   // Wednesday
    }

    [Fact]
    public void EmployeeRotation_AppliesWhenTheyAreOnNoShift()
    {
        var s = EffectiveShift.Resolve(Emp(cycle: 3, onDays: 1, anchor: "2026-07-01"), null, Loc());

        Assert.True(s.IsWorkingDay(new DateOnly(2026, 7, 1)));
        Assert.False(s.IsWorkingDay(new DateOnly(2026, 7, 2)));
        Assert.False(s.IsWorkingDay(new DateOnly(2026, 7, 3)));
        Assert.True(s.IsWorkingDay(new DateOnly(2026, 7, 4)));
    }

    [Fact]
    public void EmployeeHoursDoNotPartiallyOverrideAShift()
    {
        // Only the end is set on the employee. A field-by-field merge would give 21:00–20:00, which is
        // an overnight shift by the end<start convention and would invert every night's calculation.
        var s = EffectiveShift.Resolve(Emp(end: "20:00"), Shift(start: "21:00", end: "07:00"), Loc());

        Assert.Equal(new TimeOnly(7, 0), s.End);
        Assert.True(s.IsOvernight);
    }

    [Fact]
    public void WorkCycleApply_WritesToAScheduleTheSameWayItDoesToAnEmployee()
    {
        // Both implement IHasWorkCycle precisely so one route into the data cannot accept a rotation
        // the other rejects.
        var schedule = new Schedule();
        Assert.Equal("WorkCycleAnchorRequired", WorkCycle.Apply(schedule, 2, 1, null));
        Assert.Null(WorkCycle.Apply(schedule, 2, 1, new DateOnly(2026, 7, 1)));
        Assert.Equal(2, schedule.WorkCycleDays);

        Assert.Null(WorkCycle.Apply(schedule, null, null, null));
        Assert.Null(schedule.WorkCycleDays);
        Assert.Null(schedule.WorkCycleAnchor);
    }
}
