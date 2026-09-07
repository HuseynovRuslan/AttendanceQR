using AttendanceQR.Application.Reporting;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The rule that decides whether somebody's shift looks wrong.
///
/// The real case is the first test: Yaiçnikov charges machines at Heydər Əliyev Mərkəzi from eight in
/// the evening, and was carried on a nine-to-six day shift. Eleven hours apart, every single night,
/// and nothing anywhere said so until a day of his work had already been scored as zero.
/// </summary>
public class ShiftFitTests
{
    [Fact]
    public void The_night_worker_on_a_day_shift_is_eleven_hours_out()
    {
        var gap = ShiftFit.GapHours(new TimeOnly(19, 37), new TimeOnly(9, 0));
        Assert.Equal(11, gap);
        Assert.True(ShiftFit.IsOff(new TimeOnly(19, 37), new TimeOnly(9, 0)));
    }

    [Fact]
    public void Distance_is_measured_the_short_way_round_the_clock()
    {
        // 23:00 and 01:00 are two hours apart. A subtraction that does not wrap says twenty-two, which
        // would flag every night shift in the company and hide the ones that are genuinely misfiled.
        Assert.Equal(2, ShiftFit.GapHours(new TimeOnly(23, 0), new TimeOnly(1, 0)));
        Assert.Equal(2, ShiftFit.GapHours(new TimeOnly(1, 0), new TimeOnly(23, 0)));
        Assert.Equal(1, ShiftFit.GapHours(new TimeOnly(0, 30), new TimeOnly(23, 30)));
    }

    [Fact]
    public void Twelve_hours_is_the_furthest_two_times_can_be_apart()
    {
        Assert.Equal(12, ShiftFit.GapHours(new TimeOnly(6, 0), new TimeOnly(18, 0)));
        Assert.Equal(12, ShiftFit.GapHours(new TimeOnly(18, 0), new TimeOnly(6, 0)));
    }

    [Fact]
    public void Arriving_early_is_not_a_mismatch()
    {
        // Two hours before the shift is ordinary — traffic, an early crew, a standing arrangement.
        // A report that fires on ordinary behaviour is one people learn to close without reading.
        Assert.False(ShiftFit.IsOff(new TimeOnly(6, 0), new TimeOnly(8, 0)));
        Assert.False(ShiftFit.IsOff(new TimeOnly(11, 30), new TimeOnly(8, 0)));
        // Exactly four hours is where it stops being a variation on the same shift.
        Assert.True(ShiftFit.IsOff(new TimeOnly(12, 0), new TimeOnly(8, 0)));
    }

    [Fact]
    public void A_night_worker_on_the_right_night_shift_is_never_flagged()
    {
        // The point of the whole thing: correctly configured night work must be invisible here, or the
        // report is noise and gets ignored — with the real mismatches inside it.
        Assert.False(ShiftFit.IsOff(new TimeOnly(19, 37), new TimeOnly(20, 0)));
        Assert.False(ShiftFit.IsOff(new TimeOnly(20, 15), new TimeOnly(20, 0)));
        Assert.False(ShiftFit.IsOff(new TimeOnly(23, 50), new TimeOnly(0, 0)));
    }

    [Theory]
    [InlineData(3, 2, true)]    // majority of three
    [InlineData(3, 1, false)]   // one odd day out of three is a story, not a pattern
    [InlineData(2, 2, false)]   // too few scans to conclude anything at all
    [InlineData(20, 10, false)] // exactly half is not a majority
    [InlineData(20, 11, true)]
    [InlineData(5, 0, false)]
    public void Only_a_majority_of_mismatched_arrivals_is_worth_asking_about(int scans, int off, bool flagged)
    {
        Assert.Equal(flagged, ShiftFit.ShouldFlag(scans, off));
    }

    [Fact]
    public void Somebody_covering_a_night_once_a_month_does_not_live_on_the_list()
    {
        // Twenty ordinary days and one night shift covered for a colleague. Real, common, and not a
        // scheduling error — the person is behaving correctly and their shift is right.
        Assert.False(ShiftFit.ShouldFlag(scans: 21, offScans: 1));
    }

    // ── The second fingerprint ────────────────────────────────────────────────────────────────
    // Xaliqov İsa, Qala Anbar, September 2026. A 20:00–08:00 guard on the branch's 09:00–18:00,
    // because no shift was ever assigned to him. His morning exit opens a new day and his evening
    // arrival closes it, so every day is stored as one impossible shift — and his ARRIVALS
    // (04:46, 05:21, 06:32) sit within four hours of 09:00, so the arrival rule never saw him.

    private static readonly TimeSpan DayShift = TimeSpan.FromHours(9); // 09:00–18:00

    [Fact]
    public void A_day_that_runs_from_before_dawn_to_after_dark_is_two_different_nights()
    {
        // 01.09: in 04:46, out 22:01. Seventeen hours, most of them spent asleep at home.
        Assert.True(ShiftFit.IsSplitNight(new TimeOnly(4, 46), new TimeOnly(22, 1), DayShift));
        Assert.True(ShiftFit.IsSplitNight(new TimeOnly(5, 21), new TimeOnly(22, 51), DayShift));
        Assert.True(ShiftFit.IsSplitNight(new TimeOnly(6, 32), new TimeOnly(20, 6), DayShift));
    }

    [Fact]
    public void His_arrivals_alone_would_never_have_flagged_him()
    {
        // Why the second fingerprint had to exist: judged as arrivals against 09:00, these are
        // 4h14, 3h39 and 2h28 out — and only the first clears the four-hour bar.
        var nine = new TimeOnly(9, 0);
        Assert.True(ShiftFit.IsOff(new TimeOnly(4, 46), nine));
        Assert.False(ShiftFit.IsOff(new TimeOnly(5, 21), nine));
        Assert.False(ShiftFit.IsOff(new TimeOnly(6, 32), nine));
        Assert.False(ShiftFit.ShouldFlag(scans: 5, offScans: 2)); // and so he stayed invisible
    }

    [Fact]
    public void An_ordinary_long_day_is_not_a_split_night()
    {
        // 08:00 to 19:30 is a long day, and long days happen. It is not pre-dawn, and it is not
        // twelve hours — a report that fires on this is one people learn to close.
        Assert.False(ShiftFit.IsSplitNight(new TimeOnly(8, 0), new TimeOnly(19, 30), DayShift));
        // Pre-dawn but home before dark: an early crew, nothing to ask about.
        Assert.False(ShiftFit.IsSplitNight(new TimeOnly(5, 0), new TimeOnly(14, 0), DayShift));
        // Late finish after a normal start.
        Assert.False(ShiftFit.IsSplitNight(new TimeOnly(9, 5), new TimeOnly(23, 0), DayShift));
    }

    [Fact]
    public void A_twelve_hour_shift_is_not_flagged_for_working_its_own_hours()
    {
        // Camaşırxana Laçın's day crew is 09:00–19:00, and a driver on a real 07:00–19:30 rota is
        // working the hours they were given. The span has to exceed the ASSIGNED shift, not just
        // the clock, or this report accuses every long shift in the company.
        var twelve = TimeSpan.FromHours(12);
        Assert.False(ShiftFit.IsSplitNight(new TimeOnly(6, 30), new TimeOnly(19, 0), twelve));
    }

    [Fact]
    public void Two_such_days_are_enough_to_ask()
    {
        // Deliberately not a majority: the shape is specific enough that waiting for one would
        // leave the person hidden for another month, with their rest days scored as Qayıb.
        Assert.Equal(2, ShiftFit.MinSplitNightDays);
    }
}
