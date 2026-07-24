using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The rotation ("növbə") maths and the rules for writing one.
///
/// Worth testing closely because the failure is silent and expensive: a cycle that resolves the wrong
/// way marks someone Absent on a day they were resting, and an unexcused absence is deducted from
/// their salary in the payroll report. Nobody notices until payday.
/// </summary>
public class WorkCycleTests
{
    // Mon–Sat, Sunday off — the production default every location carries.
    private const int MonToSat = 126;

    private static bool Works(int? days, int onDays, DateOnly? anchor, DateOnly date, int mask = MonToSat)
        => AttendanceCalculator.IsScheduledWorkingDay(days, onDays, anchor, mask, date);

    // 2026-07-01 is a Wednesday.
    private static DateOnly D(int day) => new(2026, 7, day);

    [Fact]
    public void NoCycle_FallsBackToTheWeeklyMask()
    {
        // 2026-07-05 is a Sunday, which the default mask has off; the 6th is a Monday.
        Assert.False(Works(null, 1, null, D(5)));
        Assert.True(Works(null, 1, null, D(6)));
    }

    [Fact]
    public void EveryOtherDay_AlternatesAndIgnoresTheWeeklyMask()
    {
        // Anchor on the 1st: works 1, 3, 5, 7 … The 5th is a SUNDAY, which the weekly mask calls a
        // day off — a rotation replaces the mask outright, so the Sunday is a working day here.
        foreach (var day in new[] { 1, 3, 5, 7, 9 })
            Assert.True(Works(2, 1, D(1), D(day)), $"{day} should be a working day");
        foreach (var day in new[] { 2, 4, 6, 8, 10 })
            Assert.False(Works(2, 1, D(1), D(day)), $"{day} should be a rest day");
    }

    [Fact]
    public void EveryOtherDay_DoesNotDriftOverAMonth()
    {
        // The whole reason a weekly mask cannot express this: after four weeks the pattern has moved
        // to the other half of the week and must still be right.
        Assert.True(Works(2, 1, D(1), new DateOnly(2026, 7, 31)));    // +30, even → on
        Assert.False(Works(2, 1, D(1), new DateOnly(2026, 8, 1)));    // +31, odd  → off
        Assert.False(Works(2, 1, D(1), new DateOnly(2026, 12, 31)));  // +183, odd → off, half a year later
    }

    [Fact]
    public void Sutka_OneOnTwoOff()
    {
        // 24 hours on, 48 off — the guard rotation.
        Assert.True(Works(3, 1, D(1), D(1)));
        Assert.False(Works(3, 1, D(1), D(2)));
        Assert.False(Works(3, 1, D(1), D(3)));
        Assert.True(Works(3, 1, D(1), D(4)));
    }

    [Fact]
    public void TwoOnTwoOff()
    {
        Assert.True(Works(4, 2, D(1), D(1)));
        Assert.True(Works(4, 2, D(1), D(2)));
        Assert.False(Works(4, 2, D(1), D(3)));
        Assert.False(Works(4, 2, D(1), D(4)));
        Assert.True(Works(4, 2, D(1), D(5)));
    }

    [Fact]
    public void DatesBeforeTheAnchor_LandOnTheRightHalfOfTheCycle()
    {
        // A rotation set up today still has to explain LAST month's timesheet, so the maths must run
        // backwards too. C#'s % keeps the dividend's sign, which would push these outside [0, cycle)
        // and read every one of them as a rest day.
        var anchor = D(10);
        Assert.True(Works(2, 1, anchor, D(8)));
        Assert.False(Works(2, 1, anchor, D(9)));
        Assert.True(Works(2, 1, anchor, D(2)));
    }

    [Fact]
    public void IncompleteCycle_FallsBackToTheMaskRatherThanMarkingEveryDayAbsent()
    {
        // A cycle with no anchor has no day 0. Reading it as "never works" would quietly zero out
        // someone's month, so an unusable rotation is ignored instead.
        Assert.True(Works(2, 1, null, D(6)));    // Monday, per the mask
        Assert.False(Works(2, 1, null, D(5)));   // Sunday, per the mask
        Assert.True(Works(1, 1, D(1), D(6)));    // cycle < 2 is not a cycle
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(7, 3)]
    public void EveryCycle_WorksExactlyOnDaysOutOfEveryCycleLength(int days, int onDays)
    {
        var anchor = D(1);
        var worked = Enumerable.Range(0, days * 5)
            .Count(i => Works(days, onDays, anchor, anchor.AddDays(i)));

        Assert.Equal(onDays * 5, worked);
    }

    // --- writing a rotation -----------------------------------------------------

    [Fact]
    public void Apply_NullDays_ClearsEverything()
    {
        // Re-enabling months later must not inherit a stale anchor and land the person on the wrong
        // half of the cycle, so clearing wipes all three fields, not just the length.
        var e = new Employee { WorkCycleDays = 2, WorkCycleOnDays = 1, WorkCycleAnchor = D(1) };

        Assert.Null(WorkCycle.Apply(e, null, null, null));

        Assert.Null(e.WorkCycleDays);
        Assert.Null(e.WorkCycleAnchor);
        Assert.Equal(1, e.WorkCycleOnDays);
    }

    [Fact]
    public void Apply_CycleWithoutAnchor_IsRejected()
    {
        var e = new Employee();
        Assert.Equal("WorkCycleAnchorRequired", WorkCycle.Apply(e, 2, 1, null));
        Assert.Null(e.WorkCycleDays); // nothing written on a rejected request
    }

    [Theory]
    [InlineData(1, 1, "WorkCycleDaysInvalid")]
    [InlineData(29, 1, "WorkCycleDaysInvalid")]
    [InlineData(2, 2, "WorkCycleOnDaysInvalid")]  // a fully-worked cycle is not a rotation
    [InlineData(4, 0, "WorkCycleOnDaysInvalid")]
    public void Apply_RejectsIncoherentCycles(int days, int onDays, string expected)
    {
        var e = new Employee();
        Assert.Equal(expected, WorkCycle.Apply(e, days, onDays, D(1)));
        Assert.Null(e.WorkCycleDays);
    }

    [Fact]
    public void Apply_ValidCycle_IsWritten()
    {
        var e = new Employee();
        Assert.Null(WorkCycle.Apply(e, 4, 2, D(1)));
        Assert.Equal(4, e.WorkCycleDays);
        Assert.Equal(2, e.WorkCycleOnDays);
        Assert.Equal(D(1), e.WorkCycleAnchor);
    }

    [Fact]
    public void Apply_OmittedOnDays_DefaultsToOne()
    {
        // "bir gündən bir" is the common case and only needs a length and an anchor.
        var e = new Employee();
        Assert.Null(WorkCycle.Apply(e, 2, null, D(1)));
        Assert.Equal(1, e.WorkCycleOnDays);
    }
}
