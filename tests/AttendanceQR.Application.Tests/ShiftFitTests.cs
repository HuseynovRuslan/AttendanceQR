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
}
