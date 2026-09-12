using AttendanceQR.Application.Common;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The gate on a day's second stretch of work.
///
/// Most of these tests are about what the rule REFUSES. The feature exists for one crew of five, and
/// the whole design rests on the other six hundred people being unable to reach it at all — so the
/// refusals are the load-bearing half, and a change that makes one of them pass has broken the
/// promise the feature was built on.
/// </summary>
public class SplitShiftRulesTests
{
    private static TimeOnly At(int h, int m = 0) => new(h, m);

    private static readonly TimeOnly NightStart = At(22);
    private static readonly TimeOnly NightEnd = At(7);     // crosses midnight

    private static bool May(TimeOnly now, int blocks = 1, bool anyOpen = false, bool hasWindow = true)
        => SplitShiftRules.MayOpenSecondBlock(
            now, hasWindow,
            hasWindow ? NightStart : null,
            hasWindow ? NightEnd : null,
            blocks, anyOpen);

    // --- the refusals that protect everybody else ---------------------------

    [Fact]
    public void An_ordinary_shift_can_never_open_a_second_block()
    {
        // THE property the whole design rests on. A shift with no second window is every shift in the
        // company but one; if this ever returns true, the third-scan refusal is gone for all of them
        // and a stray evening retry becomes a phantom night.
        foreach (var hour in Enumerable.Range(0, 24))
            Assert.False(May(At(hour), hasWindow: false));
    }

    [Fact]
    public void A_day_that_already_has_its_two_blocks_refuses_a_third()
    {
        Assert.False(May(At(23), blocks: 2));
    }

    [Fact]
    public void An_open_block_is_never_added_to()
    {
        // Somebody still checked in is checked OUT, not given a second stretch — otherwise a night
        // worker's morning scan would open a block instead of closing the one they are in.
        Assert.False(May(At(23), blocks: 1, anyOpen: true));
    }

    [Fact]
    public void With_no_finished_block_yet_this_is_not_a_second_block_at_all()
    {
        // The ordinary first check-in does not come through this door.
        Assert.False(May(At(23), blocks: 0));
    }

    [Fact]
    public void An_afternoon_mis_tap_does_not_open_the_night()
    {
        // The accident this rule exists for. At Aeroport yolu sixteen of ninety-three closed days last
        // week were under an hour because people re-tap when GPS fails; without the window test, one
        // such retry at 14:30 would open a block that stayed open until the next morning closed it as
        // a fourteen-hour shift nobody worked.
        Assert.False(May(At(12, 30)));
        Assert.False(May(At(14, 30)));
        Assert.False(May(At(17)));
        Assert.False(May(At(19, 59)));
    }

    // --- what it allows -----------------------------------------------------

    [Fact]
    public void The_crew_coming_back_at_ten_opens_the_night()
    {
        Assert.True(May(At(22)));
        Assert.True(May(At(22, 15)));
    }

    [Fact]
    public void Turning_up_early_still_counts_as_turning_up()
    {
        // Told to be back at 22:00, they arrive at 21:30 — and a shift that starts an hour early
        // because the lorry came early is ordinary, not an anomaly to be refused at the gate.
        Assert.True(May(At(21, 30)));
        Assert.True(May(At(20)));           // the full two-hour tolerance
        Assert.False(May(At(19, 59)));      // and not a minute more
    }

    [Fact]
    public void The_window_reaches_across_midnight()
    {
        // 22:00–07:00 is one stretch. A latecomer at 01:00 is still arriving for the night.
        Assert.True(May(At(23, 59)));
        Assert.True(May(At(0, 1)));
        Assert.True(May(At(3)));
        Assert.True(May(At(6, 59)));
        Assert.False(May(At(7)));           // the night has ended
        Assert.False(May(At(9)));
    }

    // --- the wrap test on its own -------------------------------------------

    [Theory]
    [InlineData(7, 8, 18, false)]    // before an ordinary window
    [InlineData(9, 9, 18, true)]     // exactly at its start
    [InlineData(17, 9, 18, true)]
    [InlineData(18, 9, 18, false)]   // the end is exclusive
    [InlineData(23, 22, 7, true)]    // inside a window that crosses midnight
    [InlineData(3, 22, 7, true)]
    [InlineData(7, 22, 7, false)]
    [InlineData(12, 22, 7, false)]
    public void A_window_that_crosses_midnight_is_still_one_stretch(int at, int start, int end, bool inside)
    {
        Assert.Equal(inside, SplitShiftRules.InWindow(At(at), At(start), At(end)));
    }
}
