using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// How a day worked in two stretches adds up.
///
/// The arithmetic itself is <see cref="AttendanceCalculator.WorkedMinutesAcross"/>'s and is tested
/// there; what these pin is that a split day is handed to it as SEPARATE stretches rather than as one
/// span from the first arrival to the last departure, and that the later stretch is flagged as a
/// rostered return. The difference is the eleven hours the crew spends at home between washing an
/// area in the morning and coming back at ten at night — measured end-to-end the day would pay for
/// all of them, and unflagged it would still pay an hour of them as travel.
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
    public void The_eleven_hours_at_home_are_not_paid_at_all()
    {
        // 07:00–11:00 and 22:00–07:00 the next morning: four hours plus nine — NOT the twenty-four
        // between the first arrival and the last departure, which is the failure this whole path
        // exists to avoid.
        //
        // And exactly thirteen. The gap rule pays a gap up to TravelGapCapMinutes because a crew sent
        // from one of the company's sites to another is working while they drive; a rostered return is
        // the other thing entirely — they went home, and the owner's ruling is that the road home is
        // their own. Hence RosteredReturn on the span rather than a limit on how long a gap may be:
        // the flag says WHAT the stretch is, and only the loaders that read a second attendance block
        // set it.
        var night = new[] { new AttendanceCalculator.WorkSpan(At(22), At(7, 0, 1), RosteredReturn: true) };

        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), night, anyExtraOpen: false);

        Assert.Equal((4 + 9) * 60, minutes);
    }

    [Fact]
    public void A_field_visit_between_the_stretches_is_still_paid_its_travel()
    {
        // The flag is per-stretch, not per-day: a double day with a dispatched visit in the middle
        // pays the drive TO the visit and nothing for the evening at home. Told apart by what each
        // stretch is — which is the whole reason this is a flag and not a rule about long gaps.
        var rest = new[]
        {
            new AttendanceCalculator.WorkSpan(At(12), At(13)),                          // a site visit
            new AttendanceCalculator.WorkSpan(At(22), At(7, 0, 1), RosteredReturn: true),
        };

        var minutes = AttendanceCalculator.MergedWorkedMinutes(Morning(), rest, anyExtraOpen: false);

        Assert.Equal((4 * 60) + AttendanceCalculator.TravelGapCapMinutes + 60 + (9 * 60), minutes);
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
    public void Only_the_gap_before_a_rostered_return_goes_unpaid()
    {
        // Same eleven hours, same two stretches — the ONLY difference is the flag. Left unset, this is
        // a crew still out on the road and the cap applies as it always has; set, it is a crew that
        // went home. If these two ever agree, the distinction has been lost and every split day is
        // quietly paying an extra hour again.
        var asTravel = new[] { new AttendanceCalculator.WorkSpan(At(22), At(23)) };
        var asReturn = new[] { new AttendanceCalculator.WorkSpan(At(22), At(23), RosteredReturn: true) };

        Assert.Equal(
            (4 * 60) + AttendanceCalculator.TravelGapCapMinutes + 60,
            AttendanceCalculator.MergedWorkedMinutes(Morning(), asTravel, anyExtraOpen: false));

        Assert.Equal(
            (4 * 60) + 60,
            AttendanceCalculator.MergedWorkedMinutes(Morning(), asReturn, anyExtraOpen: false));
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
