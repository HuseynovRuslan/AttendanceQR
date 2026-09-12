using AttendanceQR.Application.Reporting;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Whether a split day's night is visible to the machinery that closes nights.
///
/// The split shift shipped judged by one question — "does the first window cross midnight?" — and a
/// split day's first window is 07:00–11:00, so the answer was no. Everything that looks for a shift
/// still running past midnight therefore could not see the 22:00–07:00 half: the morning scan that
/// should have closed it would have opened a fresh day instead, leaving nine hours of night work
/// open for ever, and the board would have shown five men absent while they were washing a road at
/// two in the morning.
///
/// So there are two questions, not one, and they must not be conflated:
///   • CrossesIntoNextMorningOn — "could an open record from yesterday still be running?"
///   • IsOvernightOn            — "is an arrival impossible this morning?"
/// On a split day the first is true and the second is false, and the crew's own 07:00 start is the
/// reason: answer the second question with the first and they are refused their own morning.
/// </summary>
public class SplitShiftNightCloseTests
{
    private static readonly DateOnly Day = new(2026, 9, 12);

    private static EffectiveShift Shift(int startH, int endH, (int, int)? second = null)
        => new(new TimeOnly(startH, 0), new TimeOnly(endH, 0), 15, 127, null, 1, null, "n",
               SecondStart: second is { } s ? new TimeOnly(s.Item1, 0) : null,
               SecondEnd: second is { } e ? new TimeOnly(e.Item2, 0) : null);

    private static EffectiveShift SplitDay() => Shift(7, 11, (22, 7));

    [Fact]
    public void A_split_days_night_runs_into_the_next_morning()
    {
        Assert.True(SplitDay().CrossesIntoNextMorningOn(Day));
    }

    [Fact]
    public void And_yet_a_morning_arrival_on_that_same_day_is_perfectly_possible()
    {
        // The pair that keeps the crew able to start their own day at seven. If this ever returns
        // true, their morning scan is read as "a night worker's morning scan can only be an exit" and
        // refused — the feature breaking the very people it was built for.
        Assert.False(SplitDay().IsOvernightOn(Day));
    }

    [Fact]
    public void An_ordinary_day_shift_crosses_nothing()
    {
        Assert.False(Shift(7, 17).CrossesIntoNextMorningOn(Day));
    }

    [Fact]
    public void A_standing_night_shift_answers_exactly_as_it_always_did()
    {
        // Every shift without a second window must give the same answer to both questions, or this
        // change has altered somebody it was never meant to touch.
        var night = Shift(22, 7);
        Assert.True(night.CrossesIntoNextMorningOn(Day));
        Assert.Equal(night.IsOvernightOn(Day), night.CrossesIntoNextMorningOn(Day));
    }

    [Fact]
    public void A_second_window_that_ends_the_same_evening_crosses_nothing()
    {
        // 07:00–11:00 and back 18:00–21:00 is a split day that finishes before midnight. Nothing is
        // left running, and treating it as a night would keep it on the board until the small hours.
        Assert.False(Shift(7, 11, (18, 21)).CrossesIntoNextMorningOn(Day));
    }

    [Fact]
    public void The_window_is_measured_from_the_LAST_stretch_that_ends()
    {
        // A day's last end is the second window's when there is one. Measured from the morning's
        // 11:00, the "is this shift still plausibly running" cutoff fell at 13:00 — hours before the
        // crew even left for home, let alone came back.
        Assert.Equal(new TimeOnly(7, 0), SplitDay().LastEndOn(Day));
        Assert.Equal(new TimeOnly(17, 0), Shift(7, 17).LastEndOn(Day));
    }

    [Theory]
    [InlineData(2, 0, true)]     // two in the morning, still out on the road
    [InlineData(7, 0, true)]     // clocking off
    [InlineData(8, 30, true)]    // the two hours of grace
    [InlineData(9, 30, false)]   // past it — this is a forgotten check-out, not a shift
    public void The_board_keeps_the_crew_until_their_night_is_really_over(int h, int m, bool within)
    {
        Assert.Equal(within,
            ReportQueryService.WithinOvernightWindow(SplitDay(), new TimeOnly(h, m), Day));
    }
}
