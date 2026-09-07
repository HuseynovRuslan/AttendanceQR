using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Əvəzləmə»: one person on somebody else's shift for one day.
///
/// The night this was built for, in full. Nəcəfov Vüqar is on «FM 2-ci növbə 13:00–23:00» — a DAY
/// shift, because its end is later than its start. On Saturday 5 September he covered the night guard:
/// in at 21:33, out at 06:51 on Sunday morning. The scan path's overnight rule — a morning scan closes
/// the shift that began the previous evening — is gated on the shift BEING overnight, so against his
/// own 13:00–23:00 it never ran. Saturday stayed open at zero hours, and the 06:51 exit opened a
/// fresh check-in on Sunday, which was his rest day. Nine hours and a rest day, gone in one night.
///
/// What is pinned here is the shape of the fix: an override changes WHICH schedule answers for a
/// date, and nothing else. Everything downstream then applies unchanged — which is why the overnight
/// rule starts working with no special case written for cover nights at all.
/// </summary>
public class ShiftOverrideTests
{
    private static readonly Guid Vuqar = Guid.Parse("6417dd2f-dd1f-4ea5-be50-b2f41260a3e9");
    private static readonly Guid DayShift = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid NightShift = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateOnly Saturday = new(2026, 9, 5);
    private static readonly DateOnly Sunday = new(2026, 9, 6);

    private static ShiftOverrideMap MapWith(params (Guid Employee, DateOnly Date, Guid Schedule)[] rows)
        => new(rows.ToDictionary(r => (r.Employee, r.Date), r => r.Schedule));

    private static Location Branch() => new()
    {
        Id = Guid.NewGuid(), Name = "Fəvvarələr Meydanı",
        ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
        LateThresholdMinutes = 15, WorkDaysMask = 127,
    };

    private static Schedule Shift(Guid id, string name, TimeOnly start, TimeOnly end) => new()
    {
        Id = id, Name = name, ShiftStart = start, ShiftEnd = end,
        LateThresholdMinutes = 15, WorkDaysMask = 126,
    };

    private static Dictionary<Guid, Schedule> Catalogue() => new()
    {
        [DayShift] = Shift(DayShift, "FM 2-ci növbə", new TimeOnly(13, 0), new TimeOnly(23, 0)),
        [NightShift] = Shift(NightShift, "Gecə mühafizə", new TimeOnly(21, 0), new TimeOnly(7, 0)),
    };

    [Fact]
    public void With_no_override_the_person_keeps_their_own_shift()
    {
        var map = MapWith();
        Assert.Equal(DayShift, map.EffectiveScheduleId(Vuqar, Saturday, DayShift));
        Assert.Null(map.On(Vuqar, Saturday));
        Assert.True(map.IsEmpty);
    }

    [Fact]
    public void An_override_replaces_the_shift_for_THAT_DAY_only()
    {
        // The whole point: Saturday is the cover, Sunday is his own life again.
        var map = MapWith((Vuqar, Saturday, NightShift));

        Assert.Equal(NightShift, map.EffectiveScheduleId(Vuqar, Saturday, DayShift));
        Assert.Equal(DayShift, map.EffectiveScheduleId(Vuqar, Sunday, DayShift));
    }

    [Fact]
    public void It_belongs_to_ONE_person()
    {
        // A cover is not a change to the rota. The guard who was on rest, and everybody else at the
        // branch, must be untouched by it.
        var someoneElse = Guid.NewGuid();
        var map = MapWith((Vuqar, Saturday, NightShift));

        Assert.Equal(DayShift, map.EffectiveScheduleId(someoneElse, Saturday, DayShift));
    }

    [Fact]
    public void The_covered_night_resolves_as_an_OVERNIGHT_shift()
    {
        // The bug, and the fix, in one assertion. His own shift ends AFTER it starts, so the scan
        // path's «a morning scan closes last night» rule never fired; the cover shift crosses
        // midnight, so it does.
        var schedules = Catalogue();
        var branch = Branch();
        var map = MapWith((Vuqar, Saturday, NightShift));

        var ownShift = EffectiveShift.Resolve(
            null, null, null, 0, null, schedules[DayShift], branch);
        var coverShift = EffectiveShift.Resolve(
            null, null, null, 0, null, map.ScheduleFor(Vuqar, Saturday, DayShift, schedules), branch);

        Assert.False(ownShift.IsOvernightOn(Saturday));   // 13:00 → 23:00, the night was judged by this
        Assert.True(coverShift.IsOvernightOn(Saturday));  // 21:00 → 07:00, and now it is judged by this
    }

    [Fact]
    public void An_employee_on_no_shift_at_all_can_still_be_given_a_cover()
    {
        // Most people carry no ScheduleId — their hours come from the branch. A cover night has to
        // work for them too, or the feature only serves the half of the company already on a rota.
        var schedules = Catalogue();
        var map = MapWith((Vuqar, Saturday, NightShift));

        var resolved = map.ScheduleFor(Vuqar, Saturday, ownScheduleId: null, schedules);

        Assert.NotNull(resolved);
        Assert.Equal(NightShift, resolved!.Id);
    }

    [Fact]
    public void A_missing_schedule_falls_back_rather_than_throwing()
    {
        // The row points at a schedule an admin later deleted. The day must resolve to the branch's
        // hours — the older behaviour — not blow up the tabel for everybody in the month.
        var map = MapWith((Vuqar, Saturday, Guid.NewGuid()));

        Assert.Null(map.ScheduleFor(Vuqar, Saturday, DayShift, Catalogue()));
    }
}
