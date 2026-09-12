using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// How a day worked in two stretches adds up.
///
/// The arithmetic itself is <see cref="AttendanceCalculator.WorkedMinutesAcross"/>'s and is tested
/// there; what these pin is that a split day is handed to it as SEPARATE stretches rather than as one
/// span from the first arrival to the last departure. The difference is the eleven hours the crew
/// spends at home between washing an area in the morning and coming back at ten at night — measured
/// end-to-end, the day would pay for them.
/// </summary>
public class SplitShiftDayShapeTests
{
    private static DateTime At(int hour, int minute = 0, int dayOffset = 0)
        => new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc).AddDays(dayOffset).AddHours(hour).AddMinutes(minute);

    private static AttendanceRecord Morning() => new()
    {
        Id = Guid.NewGuid(),
        CheckInAtUtc = At(7),
        CheckOutAtUtc = At(11),
    };

    [Fact]
    public void The_eleven_hours_at_home_are_not_paid_except_for_the_travel_cap()
    {
        // 07:00–11:00 and 22:00–07:00 the next morning: four hours plus nine — NOT the twenty-four
        // between the first arrival and the last departure, which is the failure this whole path
        // exists to avoid.
        //
        // Plus one hour. The gap rule was written for a crew hopping between two of the company's own
        // sites, and it pays any gap up to TravelGapCapMinutes rather than dropping it, deliberately,
        // to avoid a cliff at fifty-nine minutes. An eleven-hour gap is not travel — they went home —
        // so the cap is credited to a day that did not earn it, once per double day, per person.
        //
        // Pinned here as the CURRENT rule rather than silently corrected: changing it moves the
        // figure for every field-visit day in the product, which is a decision for the owner and not
        // a detail of this feature.
        var night = new[] { new AttendanceCalculator.WorkSpan(At(22), At(7, 0, 1)) };

        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), night, anyExtraOpen: false);

        Assert.Equal(((4 + 9) * 60) + AttendanceCalculator.TravelGapCapMinutes, minutes);
    }

    [Fact]
    public void A_short_gap_between_stretches_is_still_paid_as_travel()
    {
        // The other direction, and it is deliberate: a crew that finishes one site at 09:55 and scans
        // in at the next at 10:40 was working in between. The cap is what stops that logic paying for
        // a whole evening at home — see TravelGapCapMinutes.
        var second = new[] { new AttendanceCalculator.WorkSpan(At(11, 30), At(15, 30)) };

        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), second, anyExtraOpen: false);

        Assert.Equal((4 * 60) + 30 + (4 * 60), minutes);   // 4h + the half-hour gap + 4h
    }

    [Fact]
    public void A_gap_longer_than_the_cap_pays_only_the_cap()
    {
        var night = new[] { new AttendanceCalculator.WorkSpan(At(22), At(23)) };

        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), night, anyExtraOpen: false);

        Assert.Equal((4 * 60) + AttendanceCalculator.TravelGapCapMinutes + 60, minutes);
    }

    [Fact]
    public void An_unfinished_second_stretch_adds_nothing_yet()
    {
        // The night is still running. Its minutes land when it is closed — the same rule every other
        // open day follows, and the reason the board says «İşdə» rather than showing a growing figure.
        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), [], anyExtraOpen: true);

        Assert.Null(minutes);
    }

    [Fact]
    public void Two_stretches_that_overlap_are_counted_once()
    {
        // Cannot happen through the scan path — the database forbids two open blocks — but a
        // hand-corrected time could produce it, and paying the overlap twice would be a gift nobody
        // could explain.
        var overlapping = new[] { new AttendanceCalculator.WorkSpan(At(10), At(14)) };

        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), overlapping, anyExtraOpen: false);

        Assert.Equal(7 * 60, minutes);   // 07:00–14:00, not 4h + 4h
    }
}
