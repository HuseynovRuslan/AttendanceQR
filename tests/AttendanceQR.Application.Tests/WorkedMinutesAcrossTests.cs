using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A day is no longer one scan pair. Pərviz drives to a park with no poster, checks in and out there,
/// then comes to the base and scans the poster — and until this existed the reporting answered that
/// with whichever half it happened to prefer: an office scan hid the field hours completely, and a
/// field-only day was measured first-arrival-to-last-departure, quietly paying the whole middle of a
/// day that held two twenty-minute visits.
///
/// The rule these pin: overlapping stretches count once, and the gap between two stretches is paid up
/// to <see cref="AttendanceCalculator.TravelGapCapMinutes"/> — an honest drive between two of the
/// company's own sites is work, five idle hours are not.
/// </summary>
public class WorkedMinutesAcrossTests
{
    private static readonly DateTime Day = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
    private static AttendanceCalculator.WorkSpan Span(double fromHour, double toHour) =>
        new(Day.AddHours(fromHour), Day.AddHours(toHour));

    private static int Minutes(params AttendanceCalculator.WorkSpan[] spans) =>
        AttendanceCalculator.WorkedMinutesAcross(spans);

    [Fact]
    public void One_stretch_is_just_its_length()
    {
        Assert.Equal(480, Minutes(Span(9, 17)));
    }

    [Fact]
    public void Nothing_at_all_is_zero_not_a_crash()
    {
        Assert.Equal(0, AttendanceCalculator.WorkedMinutesAcross(Array.Empty<AttendanceCalculator.WorkSpan>()));
    }

    // --- the case this was built for -----------------------------------------

    [Fact]
    public void Pervizs_day_field_then_a_drive_then_the_office()
    {
        // 09:34–09:55 at the park, 45 minutes on the road, 10:40–18:00 at the base.
        // 21 + 45 + 440 = 506. The drive is inside the cap, so it is paid in full.
        var minutes = Minutes(
            new(Day.AddHours(9).AddMinutes(34), Day.AddHours(9).AddMinutes(55)),
            new(Day.AddHours(10).AddMinutes(40), Day.AddHours(18)));

        Assert.Equal(506, minutes);
    }

    [Fact]
    public void A_gap_longer_than_the_cap_pays_only_the_cap()
    {
        // The abuse shape, and the reason a plain first-in-to-last-out span was refused: a
        // twenty-minute visit at 09:34 and an office arrival at 15:00 would otherwise pay 8½ hours.
        // 20 + 60 (capped from 5h05) + 180 = 260.
        var minutes = Minutes(
            new(Day.AddHours(9).AddMinutes(34), Day.AddHours(9).AddMinutes(54)),
            new(Day.AddHours(15), Day.AddHours(18)));

        Assert.Equal(260, minutes);
        // And emphatically NOT the whole span.
        Assert.NotEqual((int)(Day.AddHours(18) - Day.AddHours(9).AddMinutes(34)).TotalMinutes, minutes);
    }

    [Fact]
    public void The_cap_is_a_ceiling_not_a_cliff()
    {
        // A minute either side of the cap must not change the answer by an hour — a rule where 59
        // minutes pays 59 and 61 pays nothing is the one guaranteed to feel arbitrary to whoever
        // loses by it.
        var justUnder = Minutes(Span(9, 10), new(Day.AddHours(10).AddMinutes(59), Day.AddHours(12)));
        var justOver = Minutes(Span(9, 10), new(Day.AddHours(11).AddMinutes(1), Day.AddHours(12)));

        Assert.InRange(justUnder - justOver, -3, 3);
    }

    // --- overlap: the thing that must never be paid twice ---------------------

    [Fact]
    public void A_visit_nested_inside_office_hours_is_counted_once()
    {
        // Kamran's real shape: office 07:23–17:40 with a field visit 11:00–17:40 inside it. The day
        // is 10h17, not 10h17 plus another 6h40.
        var minutes = Minutes(
            new(Day.AddHours(7).AddMinutes(23), Day.AddHours(17).AddMinutes(40)),
            new(Day.AddHours(11), Day.AddHours(17).AddMinutes(40)));

        Assert.Equal(617, minutes);
    }

    [Fact]
    public void Partly_overlapping_stretches_merge_into_one()
    {
        Assert.Equal(300, Minutes(Span(9, 13), Span(12, 14)));
    }

    [Fact]
    public void Touching_stretches_add_no_travel()
    {
        // Out of one and into the next at the same instant — there is no journey to pay for.
        Assert.Equal(480, Minutes(Span(9, 13), Span(13, 17)));
    }

    [Fact]
    public void Order_does_not_matter()
    {
        Assert.Equal(Minutes(Span(9, 11), Span(14, 17)), Minutes(Span(14, 17), Span(9, 11)));
    }

    // --- data that should not be trusted --------------------------------------

    [Fact]
    public void A_zero_length_visit_contributes_nothing_and_invents_no_travel()
    {
        // Real rows look like this: a force-checkout stamps check-in and check-out at the same
        // instant. It is not a stretch of presence, so it must not become one — nor may it earn an
        // hour of "travel" to the office scan beside it.
        var instant = Day.AddHours(18).AddMinutes(6);
        Assert.Equal(480, Minutes(Span(9, 17), new(instant, instant)));
    }

    [Fact]
    public void An_inverted_span_is_ignored_rather_than_guessed_at()
    {
        // Check-out before check-in means the data is wrong (a hand-edit, most likely). Dropping it
        // is honest; clamping it to zero-length would be the same answer with a pretence of meaning.
        Assert.Equal(480, Minutes(Span(9, 17), new(Day.AddHours(15), Day.AddHours(14))));
    }

    [Fact]
    public void Three_short_visits_pay_their_work_plus_two_capped_drives()
    {
        // 60 + 60 + 60 of work, two 3-hour gaps → 60 each. 180 + 120 = 300.
        var minutes = Minutes(Span(8, 9), Span(12, 13), Span(16, 17));
        Assert.Equal(300, minutes);
    }

    // --- the shared decision both paths make ---------------------------------
    //
    // These pin MergedWorkedMinutes rather than the raw span merge: it is the function whose
    // existence stops the live board and the nightly job from disagreeing, and every "null" below is
    // a case where the caller must keep whatever Compute already decided.

    private static AttendanceRecord Office(double fromHour, double? toHour) => new()
    {
        CheckInAtUtc = Day.AddHours(fromHour),
        CheckOutAtUtc = toHour is double t ? Day.AddHours(t) : null,
    };

    [Fact]
    public void No_field_visits_changes_nothing()
    {
        Assert.Null(AttendanceCalculator.MergedWorkedMinutes(Office(9, 17), Array.Empty<AttendanceCalculator.WorkSpan>(), false));
        Assert.Null(AttendanceCalculator.MergedWorkedMinutes(null, Array.Empty<AttendanceCalculator.WorkSpan>(), false));
    }

    [Fact]
    public void A_field_only_day_is_its_visits()
    {
        Assert.Equal(120, AttendanceCalculator.MergedWorkedMinutes(null, new[] { Span(9, 11) }, false));
    }

    [Fact]
    public void A_field_only_day_with_an_open_visit_is_left_to_Compute()
    {
        // Incomplete, and Incomplete is zero — measuring the closed visits would pay a day that is
        // still, as far as anyone knows, being worked.
        Assert.Null(AttendanceCalculator.MergedWorkedMinutes(null, new[] { Span(9, 11) }, anyFieldOpen: true));
    }

    [Fact]
    public void A_mixed_day_is_the_union_of_both_halves()
    {
        // 2h field + 60 min of the hour-long drive + 5h office.
        Assert.Equal(480, AttendanceCalculator.MergedWorkedMinutes(Office(12, 17), new[] { Span(9, 11) }, false));
    }

    [Fact]
    public void An_office_day_still_open_is_left_to_Compute()
    {
        // Nothing to merge into: an unclosed day is zero either way, and inventing minutes for it
        // would hide the very thing /admin/open-records exists to surface.
        Assert.Null(AttendanceCalculator.MergedWorkedMinutes(Office(9, null), new[] { Span(12, 13) }, false));
    }

    [Fact]
    public void A_still_open_field_visit_never_stops_a_closed_office_day_from_merging()
    {
        // The open visit contributes no span (it has no end); the closed ones still count, and the
        // office half is untouched. anyFieldOpen must not veto a mixed day the way it vetoes a
        // field-only one.
        Assert.Equal(480, AttendanceCalculator.MergedWorkedMinutes(Office(12, 17), new[] { Span(9, 11) }, anyFieldOpen: true));
    }

    [Fact]
    public void The_cap_is_per_gap_not_per_day()
    {
        // Two separate journeys are two journeys. This is deliberate and worth stating: someone
        // sent to three sites drives three times, and one shared allowance would under-pay them.
        Assert.Equal(300, Minutes(Span(8, 9), Span(12, 13), Span(16, 17)));
        Assert.Equal(180, Minutes(Span(8, 9), Span(12, 13)));
    }
}
