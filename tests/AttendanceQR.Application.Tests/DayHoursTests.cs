using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Hours that differ by day of the week.
///
/// A shift had one start and one end, and a real crew at Heydər Əliyev Mərkəzi works 08:00–18:00 from
/// Monday to Friday and 09:00–18:00 at the weekend. An employee holds one schedule, so there was
/// nowhere to put the second pair — the choice was to measure every weekend against the weekday clock
/// (an hour of invented lateness on each of them) or every weekday against the weekend one.
///
/// Two things are load-bearing here. The parse must never throw, because it runs inside the nightly
/// summary job and an exception there is a whole company's attendance going unwritten; and a day with
/// no entry must fall back to the shift's ordinary hours, because that is every day of almost every
/// shift in the system.
/// </summary>
public class DayHoursTests
{
    private static Location Loc() => new()
    {
        Name = "L", Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
        ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
        LateThresholdMinutes = 15, WorkDaysMask = 126,
    };

    /// <summary>08:00–18:00, except 09:00–18:00 on Saturday and Sunday — the real one.</summary>
    private static Schedule Weekday8Weekend9() => new()
    {
        Name = "HƏM Təmizlik",
        ShiftStart = new TimeOnly(8, 0),
        ShiftEnd = new TimeOnly(18, 0),
        LateThresholdMinutes = 15,
        WorkDaysMask = 127,
        DayHours = "0=09:00-18:00;6=09:00-18:00",
    };

    // 2026-09-04 is a Friday, 05 Saturday, 06 Sunday, 07 Monday.
    private static readonly DateOnly Friday = new(2026, 9, 4);
    private static readonly DateOnly Saturday = new(2026, 9, 5);
    private static readonly DateOnly Sunday = new(2026, 9, 6);

    [Fact]
    public void The_weekend_starts_an_hour_later_and_the_week_does_not()
    {
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, Weekday8Weekend9(), Loc());

        Assert.Equal(new TimeOnly(8, 0), shift.HoursOn(Friday).Start);
        Assert.Equal(new TimeOnly(9, 0), shift.HoursOn(Saturday).Start);
        Assert.Equal(new TimeOnly(9, 0), shift.HoursOn(Sunday).Start);
        // The end is the same all week here, and must not be disturbed by the override.
        Assert.Equal(new TimeOnly(18, 0), shift.HoursOn(Saturday).End);
    }

    [Fact]
    public void A_day_with_no_entry_keeps_the_shifts_own_hours()
    {
        // Almost every day of almost every shift. If this ever stopped being true the change would be
        // invisible on the screen and wrong in the payroll.
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, Weekday8Weekend9(), Loc());

        foreach (var d in new[] { Friday, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 8) })
            Assert.Equal((new TimeOnly(8, 0), new TimeOnly(18, 0)), shift.HoursOn(d));
    }

    [Fact]
    public void A_shift_with_no_overrides_at_all_answers_the_same_every_day()
    {
        var plain = new Schedule
        {
            Name = "Gündüz", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            WorkDaysMask = 126,
        };
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, plain, Loc());

        Assert.Equal(shift.HoursOn(Friday), shift.HoursOn(Saturday));
    }

    [Fact]
    public void An_employee_override_and_a_location_carry_no_per_day_hours()
    {
        // Only a schedule can hold them; the other two sources are one pair of times and always were.
        // Worth pinning: silently reading a stale spec onto a shift resolved from the location would
        // apply one crew's weekend to somebody who is not on it.
        var shift = EffectiveShift.Resolve(new TimeOnly(7, 0), new TimeOnly(16, 0), null, 1, null, null, Loc());

        Assert.Equal((new TimeOnly(7, 0), new TimeOnly(16, 0)), shift.HoursOn(Saturday));
    }

    [Fact]
    public void Overnight_is_decided_per_day()
    {
        // A shift whose ordinary hours are a day shift can still have one night in it, and the board's
        // carry-over reads this to decide whether yesterday's shift may still be running.
        var s = new Schedule
        {
            Name = "Qarışıq", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            WorkDaysMask = 127, DayHours = "6=21:00-07:00",
        };
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, s, Loc());

        Assert.False(shift.IsOvernightOn(Friday));
        Assert.True(shift.IsOvernightOn(Saturday));
        // The base flag still answers about the ordinary hours, which is what its callers mean.
        Assert.False(shift.IsOvernight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("rubbish")]
    [InlineData("9=09:00-18:00")]      // no such day
    [InlineData("-1=09:00-18:00")]     // nor this one
    [InlineData("6=09:00")]            // no end
    [InlineData("6=25:00-18:00")]      // not a time
    [InlineData("=09:00-18:00")]       // no day
    public void A_broken_spec_is_ignored_rather_than_thrown(string? spec)
    {
        // This parse runs inside the nightly summary job. An exception from a column somebody
        // hand-edited would take a whole company's attendance with it, so the worst a bad string may
        // do is leave the day on the shift's ordinary hours.
        var s = new Schedule
        {
            Name = "X", ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0), DayHours = spec,
        };
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, s, Loc());

        Assert.Equal((new TimeOnly(8, 0), new TimeOnly(18, 0)), shift.HoursOn(Saturday));
    }

    [Theory]
    [InlineData("6=09:00-18:00;")]      // a trailing separator
    [InlineData(" 6 = 09:00 - 18:00 ")] // spaces somebody typed
    public void Forgiving_where_forgiveness_costs_nothing(string spec)
    {
        // Tolerated on purpose. These are shapes a human produces, they are unambiguous, and refusing
        // them would send the day to the wrong hours for the sake of a semicolon.
        var s = new Schedule
        {
            Name = "X", ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0), DayHours = spec,
        };
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, s, Loc());

        Assert.Equal(new TimeOnly(9, 0), shift.HoursOn(Saturday).Start);
    }

    [Fact]
    public void One_bad_entry_does_not_take_the_good_ones_with_it()
    {
        var s = new Schedule
        {
            Name = "X", ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0),
            DayHours = "6=nonsense;0=09:00-18:00",
        };
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, s, Loc());

        Assert.Equal(new TimeOnly(8, 0), shift.HoursOn(Saturday).Start);  // fell back
        Assert.Equal(new TimeOnly(9, 0), shift.HoursOn(Sunday).Start);    // survived
    }

    [Fact]
    public void Round_trips_through_the_stored_form()
    {
        var map = new Dictionary<DayOfWeek, (TimeOnly Start, TimeOnly End)>
        {
            [DayOfWeek.Saturday] = (new TimeOnly(9, 0), new TimeOnly(18, 0)),
            [DayOfWeek.Sunday] = (new TimeOnly(9, 0), new TimeOnly(18, 0)),
        };

        var text = DayHours.Format(map);

        // Days in order, so saving an unchanged form does not churn the column.
        Assert.Equal("0=09:00-18:00;6=09:00-18:00", text);
        Assert.Equal(map, DayHours.Parse(text));
    }

    [Fact]
    public void An_empty_map_stores_nothing()
    {
        Assert.Null(DayHours.Format(new Dictionary<DayOfWeek, (TimeOnly, TimeOnly)>()));
        Assert.Null(DayHours.Format(null));
        Assert.Empty(DayHours.Parse(null));
    }
}
