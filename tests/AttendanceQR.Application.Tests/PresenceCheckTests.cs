using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Mövcudluq yoxlaması» — a manager who doubts one person asks, at random moments of their day, where they are.
///
/// The rules agreed with the owner on 10.11.2026, each pinned here: off for everybody unless switched on for a
/// person, and then only for a while; at most two a day, never in the first or last twenty minutes, an hour apart;
/// fifteen minutes to answer, a late answer still taken; the manager told only about an answer from outside; and
/// none of it ever touching the day itself.
/// </summary>
public class PresenceCheckTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b7");
    private static readonly Guid OtherTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b8");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    /// <summary>A moment on 11.11.2026, Baku time (UTC+4 all year), as UTC.</summary>
    private static DateTime At(int hour, int minute, int day = 11) =>
        new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-4);

    private static readonly DateOnly Day = new(2026, 11, 11);

    // The branch: Mərkəz, 150 m. «Inside» is the point itself; «outside» a little over a kilometre north.
    private const double BranchLat = 40.3710, BranchLng = 49.8370;
    private const double FarLat = 40.3820;

    // --- the rules ----------------------------------------------------------------------------------------

    [Fact]
    public void The_window_opens_twenty_minutes_after_arriving_and_closes_twenty_before_the_shift_ends()
    {
        var window = PresenceCheckRules.Window(At(9, 2), At(9, 5), At(18, 0));

        Assert.Equal((At(9, 22), At(17, 40)), window);
    }

    [Fact]
    public void Switched_on_later_in_the_day_it_starts_from_now()
    {
        var window = PresenceCheckRules.Window(At(9, 2), At(13, 0), At(18, 0));

        Assert.Equal((At(13, 1), At(17, 40)), window);
    }

    [Fact]
    public void Checked_in_after_the_shift_ended_the_window_falls_back_to_four_hours()
    {
        // No shift end after the check-in: working outside their hours.
        var window = PresenceCheckRules.Window(At(19, 0), At(19, 5), shiftEndUtc: null);

        Assert.Equal((At(19, 20), At(23, 20)), window);
    }

    [Fact]
    public void Too_close_to_the_end_of_the_shift_there_is_no_check_at_all()
    {
        Assert.Null(PresenceCheckRules.Window(At(17, 30), At(17, 31), At(18, 0)));
    }

    [Fact]
    public void Two_checks_land_inside_the_window_at_least_an_hour_apart_whatever_the_draw()
    {
        var (start, end) = (At(9, 22), At(17, 40));
        for (var seed = 0; seed < 500; seed++)
        {
            var times = PresenceCheckRules.PlanTimes(start, end, 5, new Random(seed));

            Assert.Equal(2, times.Count);   // asked for five, the cap is two
            Assert.All(times, t => Assert.InRange(t, start, end));
            Assert.True(times[1] - times[0] >= PresenceCheckRules.MinGap, $"seed {seed}: {times[0]:t}–{times[1]:t}");
        }
    }

    [Fact]
    public void A_window_shorter_than_the_gap_gets_one_check()
    {
        var times = PresenceCheckRules.PlanTimes(At(17, 0), At(17, 40), 2, new Random(7));

        var only = Assert.Single(times);
        Assert.InRange(only, At(17, 0), At(17, 40));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(150, true)]        // on the line is inside, as at the poster
    [InlineData(150.1, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void Inside_is_the_scans_own_rule_and_never_a_distance_that_is_not_a_number(double distance, bool inside)
    {
        Assert.Equal(inside, PresenceCheckRules.IsInside(distance, 150));
    }

    [Theory]
    [InlineData(double.NaN, 49.8)]
    [InlineData(40.3, double.PositiveInfinity)]
    [InlineData(91, 49.8)]
    [InlineData(40.3, -181)]
    public void A_coordinate_that_is_not_a_position_is_refused(double lat, double lng)
    {
        Assert.False(PresenceCheckRules.IsValidFix(lat, lng, 10));
    }

    [Fact]
    public void A_night_shift_ends_the_next_morning_and_a_check_in_after_the_day_shift_has_no_end()
    {
        var night = new Location { ShiftStart = new TimeOnly(22, 0), ShiftEnd = new TimeOnly(7, 0), WorkDaysMask = 127 };
        var day = new Location { ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), WorkDaysMask = 127 };
        var person = new Employee();

        Assert.Equal(At(7, 0, day: 12), PresenceCheckSweep.ShiftEndAfter(
            EffectiveShift.Resolve(person, null, night), Day, At(22, 5), Baku));
        Assert.Null(PresenceCheckSweep.ShiftEndAfter(
            EffectiveShift.Resolve(person, null, day), Day, At(19, 0), Baku));
    }

    // --- the sweep ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Nobody_is_checked_unless_a_manager_switched_it_on_for_them()
    {
        using var w = new World();
        w.Open(w.Worker, At(9, 2));
        var push = new FakeNotifier();

        var outcome = await w.Sweep(push, At(12, 0));

        Assert.Equal(0, outcome.Planned);
        Assert.Empty(w.Db.PresenceChecks);
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task Switched_on_and_checked_in_two_checks_are_planned_inside_the_day()
    {
        using var w = new World();
        w.SwitchOn(w.Worker, until: At(9, 0, day: 25));
        w.Open(w.Worker, At(9, 2));

        var outcome = await w.Sweep(new FakeNotifier(), At(9, 10));

        Assert.Equal(2, outcome.Planned);
        var checks = w.Db.PresenceChecks.OrderBy(c => c.DueAtUtc).ToList();
        Assert.All(checks, c => Assert.InRange(c.DueAtUtc, At(9, 22), At(17, 40)));
        Assert.True(checks[1].DueAtUtc - checks[0].DueAtUtc >= PresenceCheckRules.MinGap);
        Assert.All(checks, c => Assert.Equal(Day, c.CheckDate));

        // A second pass does not plan the same day again.
        Assert.Equal(0, (await w.Sweep(new FakeNotifier(), At(9, 11))).Planned);
        Assert.Equal(2, w.Db.PresenceChecks.Count());
    }

    [Fact]
    public async Task When_the_time_runs_out_the_checks_stop()
    {
        using var w = new World();
        w.SwitchOn(w.Worker, until: At(12, 0));
        var day = w.Open(w.Worker, At(9, 2));
        var planned = w.Planned(day, At(12, 30));
        var push = new FakeNotifier();

        // Nothing new is planned after the end, and what was planned does not go out.
        w.Open(w.Other, At(9, 0));
        var outcome = await w.Sweep(push, At(12, 31));

        Assert.Equal(0, outcome.Planned);
        Assert.Empty(push.Sent);
        Assert.Equal(PresenceCheckResult.Cancelled, w.Reload(planned).Result);
        Assert.Equal(PresenceCheckRules.NoteDisabled, w.Reload(planned).Note);
    }

    [Fact]
    public async Task A_check_planned_for_a_day_that_has_closed_is_cancelled_not_sent()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var planned = w.Planned(day, At(15, 0));
        w.CheckOut(day, At(14, 0));
        var push = new FakeNotifier();

        await w.Sweep(push, At(15, 1));

        Assert.Empty(push.Sent);
        Assert.Equal(PresenceCheckResult.Cancelled, w.Reload(planned).Result);
        Assert.Equal(PresenceCheckRules.NoteDayClosed, w.Reload(planned).Note);
    }

    [Fact]
    public async Task A_due_check_is_sent_once_however_many_passes_see_it()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var planned = w.Planned(day, At(11, 0));
        var push = new FakeNotifier();

        await w.Sweep(push, At(11, 0));
        await w.Sweep(push, At(11, 1));
        await w.Sweep(push, At(11, 2));

        var sent = Assert.Single(push.Sent);
        Assert.Equal([w.Worker], sent.To);
        Assert.Equal(PresenceCheckRules.PushTitle, sent.Title);
        Assert.Equal("/home", sent.Url);
        Assert.Equal(At(11, 0), w.Reload(planned).SentAtUtc);
        Assert.Equal(1, w.Reload(planned).Reached);
    }

    [Fact]
    public async Task Somebody_out_on_a_field_visit_is_not_asked_where_they_are()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var planned = w.Planned(day, At(11, 0));
        w.Db.FieldVisits.Add(new FieldVisit
        {
            TenantId = TenantId, EmployeeId = w.Worker, VisitDate = Day, Status = FieldVisitStatus.CheckedIn,
            CheckInAtUtc = At(10, 30),
        });
        w.Db.SaveChanges();
        var push = new FakeNotifier();

        await w.Sweep(push, At(11, 0));

        Assert.Empty(push.Sent);
        Assert.Equal(PresenceCheckRules.NoteFieldVisit, w.Reload(planned).Note);
    }

    [Fact]
    public async Task A_check_far_overdue_after_an_outage_is_dropped_rather_than_fired_late()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var restartLate = w.Planned(day, At(11, 0));
        var push = new FakeNotifier();

        await w.Sweep(push, At(11, 45));

        Assert.Empty(push.Sent);
        Assert.Equal(PresenceCheckRules.NoteStale, w.Reload(restartLate).Note);

        // A deploy's couple of minutes is not an outage: that one still goes.
        var shortly = w.Planned(day, At(13, 0), slot: 1);
        await w.Sweep(push, At(13, 4));
        Assert.Single(push.Sent);
        Assert.NotNull(w.Reload(shortly).SentAtUtc);
    }

    [Fact]
    public async Task No_answer_in_fifteen_minutes_reads_no_answer_and_nobody_else_is_pushed()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));
        var push = new FakeNotifier();

        await w.Sweep(push, At(11, 14));
        Assert.Equal(PresenceCheckResult.Pending, w.Reload(check).Result);

        var outcome = await w.Sweep(push, At(11, 15));

        Assert.Equal(1, outcome.NoAnswer);
        Assert.Equal(PresenceCheckResult.NoAnswer, w.Reload(check).Result);
        // The manager hears nothing: «no answer» is counted on the profile, never pushed.
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task Leaving_before_the_answer_window_ran_out_is_not_no_answer()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(17, 30));
        w.CheckOut(day, At(17, 35));

        await w.Sweep(new FakeNotifier(), At(17, 50));

        Assert.Equal(PresenceCheckResult.Cancelled, w.Reload(check).Result);
        Assert.Equal(PresenceCheckRules.NoteDayClosed, w.Reload(check).Note);
    }

    // --- the answer -----------------------------------------------------------------------------------------

    [Fact]
    public async Task An_answer_from_the_branch_is_inside_and_the_manager_hears_nothing()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));
        var alerts = new CapturingQueue();

        var result = await w.Phone(w.Worker, At(11, 3), alerts).Answer(check.Id, new(BranchLat, BranchLng, 12));

        Assert.Equal("Inside", Json(result).GetProperty("result").GetString());
        var stored = w.Reload(check);
        Assert.Equal(PresenceCheckResult.Inside, stored.Result);
        Assert.False(stored.Late);
        Assert.Equal(150, stored.RadiusMeters);
        Assert.Empty(alerts.Items);
    }

    [Fact]
    public async Task An_answer_from_outside_is_told_to_the_manager_of_the_branch_with_the_distance()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));
        var alerts = new CapturingQueue();

        await w.Phone(w.Worker, At(11, 4), alerts).Answer(check.Id, new(FarLat, BranchLng, 15));

        Assert.Equal(PresenceCheckResult.Outside, w.Reload(check).Result);
        var alert = Assert.Single(alerts.Items);
        Assert.Equal(StaffAlertKinds.PresenceOutside, alert.Kind);
        Assert.Equal(w.Worker, alert.EmployeeId);

        var push = new FakeNotifier();
        var outcome = await StaffAlertDispatcher.HandleAsync(w.Db, push, alert, At(11, 4), Baku);

        Assert.Equal(StaffAlertOutcome.Sent, outcome);
        var sent = Assert.Single(push.Sent);
        Assert.Equal([w.ManagerA], sent.To);
        Assert.Equal("Bağırov Zamiq filialdan kənardadır", sent.Title);
        Assert.Contains("11:04", sent.Body);
        Assert.Contains("«Mərkəz» filialından", sent.Body);
        Assert.Matches(@"1[12]\d\d m uzaqda", sent.Body);
        Assert.Equal($"/admin/employees/{w.Worker}", sent.Url);
    }

    [Fact]
    public async Task A_late_answer_is_still_taken_and_marked_late()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));
        await w.Sweep(new FakeNotifier(), At(11, 20));
        Assert.Equal(PresenceCheckResult.NoAnswer, w.Reload(check).Result);

        var result = await w.Phone(w.Worker, At(11, 40)).Answer(check.Id, new(BranchLat, BranchLng, 20));

        Assert.True(Json(result).GetProperty("late").GetBoolean());
        Assert.Equal(PresenceCheckResult.Inside, w.Reload(check).Result);
        Assert.True(w.Reload(check).Late);
    }

    [Fact]
    public async Task A_second_answer_does_not_change_the_first()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));
        await w.Phone(w.Worker, At(11, 2)).Answer(check.Id, new(FarLat, BranchLng, 10));

        // Walked back to the branch and tried again.
        var again = await w.Phone(w.Worker, At(11, 9)).Answer(check.Id, new(BranchLat, BranchLng, 10));

        Assert.IsType<ConflictObjectResult>(again);
        Assert.Equal(PresenceCheckResult.Outside, w.Reload(check).Result);
    }

    [Theory]
    [InlineData(double.NaN, BranchLng)]
    [InlineData(BranchLat, double.PositiveInfinity)]
    [InlineData(95, BranchLng)]
    public async Task A_coordinate_that_is_not_a_position_is_refused_and_never_reads_as_inside(double lat, double lng)
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));

        var result = await w.Phone(w.Worker, At(11, 2)).Answer(check.Id, new(lat, lng, 10));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(PresenceCheckResult.Pending, w.Reload(check).Result);
    }

    [Fact]
    public async Task Nobody_answers_somebody_elses_check_not_in_the_same_company_and_not_from_another()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));

        var colleague = await w.Phone(w.Other, At(11, 2)).Answer(check.Id, new(BranchLat, BranchLng, 10));
        Assert.IsType<NotFoundObjectResult>(colleague);

        // The same id, asked from another company: its query filter does not even see the row.
        using var other = w.ContextFor(OtherTenantId);
        var stranger = Guid.NewGuid();
        var foreign = await new PresenceChecksController(other, new FixedClock(At(11, 2)))
        {
            ControllerContext = Context(stranger, "Employee"),
        }.Answer(check.Id, new(BranchLat, BranchLng, 10));
        Assert.IsType<NotFoundObjectResult>(foreign);

        Assert.Equal(PresenceCheckResult.Pending, w.Reload(check).Result);
    }

    [Fact]
    public async Task A_check_not_yet_asked_cannot_be_answered_ahead_of_time()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var planned = w.Planned(day, At(15, 0));

        var result = await w.Phone(w.Worker, At(11, 0)).Answer(planned.Id, new(BranchLat, BranchLng, 10));

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task After_checking_out_the_question_no_longer_takes_an_answer()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(17, 20));
        w.CheckOut(day, At(17, 30));

        var result = await w.Phone(w.Worker, At(17, 31)).Answer(check.Id, new(BranchLat, BranchLng, 10));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task A_phone_that_gives_no_position_is_gps_failed_not_outside_and_may_try_again()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));
        var alerts = new CapturingQueue();

        await w.Phone(w.Worker, At(11, 1), alerts).Answer(check.Id, new(null, null, null, "denied"));
        Assert.Equal(PresenceCheckResult.GpsFailed, w.Reload(check).Result);
        Assert.Empty(alerts.Items);

        await w.Phone(w.Worker, At(11, 3), alerts).Answer(check.Id, new(BranchLat, BranchLng, 10));
        Assert.Equal(PresenceCheckResult.Inside, w.Reload(check).Result);
    }

    [Fact]
    public async Task An_answer_never_touches_the_day_its_hours_or_its_summary()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        var check = w.Sent(day, At(11, 0));

        await w.Phone(w.Worker, At(11, 2), new CapturingQueue()).Answer(check.Id, new(FarLat, BranchLng, 10));

        var record = w.Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == day.Id);
        Assert.Equal(At(9, 2), record.CheckInAtUtc);
        Assert.Null(record.CheckOutAtUtc);
        Assert.Null(record.VoidedAtUtc);
        Assert.Equal(AttendanceStatus.OnTime, record.Status);
        Assert.Empty(w.Db.DailySummaries);
    }

    [Fact]
    public async Task The_home_page_sees_the_waiting_question_until_it_is_answered()
    {
        using var w = new World();
        var day = w.Open(w.Worker, At(9, 2));
        w.Planned(day, At(15, 0));   // planned, not asked: never shown
        var check = w.Sent(day, At(11, 0), slot: 1);

        var before = Json(await w.Phone(w.Worker, At(11, 30)).Pending()).GetProperty("check");
        Assert.Equal(check.Id, before.GetProperty("id").GetGuid());

        await w.Phone(w.Worker, At(11, 31)).Answer(check.Id, new(BranchLat, BranchLng, 10));

        var after = Json(await w.Phone(w.Worker, At(11, 32)).Pending()).GetProperty("check");
        Assert.Equal(JsonValueKind.Null, after.ValueKind);
    }

    // --- the manager's switch -----------------------------------------------------------------------------

    [Fact]
    public async Task The_manager_of_the_branch_switches_it_on_for_two_weeks_and_it_is_written_down()
    {
        using var w = new World();

        var result = await w.Panel(w.ManagerA, "Manager", At(10, 0)).Set(w.Worker, new(14));

        Assert.IsType<OkObjectResult>(result);
        var person = w.Db.Employees.AsNoTracking().Single(e => e.Id == w.Worker);
        Assert.Equal(At(10, 0).AddDays(14), person.PresenceCheckUntilUtc);
        Assert.Equal(w.ManagerA, person.PresenceCheckSetByEmployeeId);
        var audit = Assert.Single(w.Db.AuditLogs.Where(a => a.EventType == AuditEventType.PresenceCheckSettingChanged));
        Assert.Equal(w.Worker, audit.EmployeeId);
        Assert.StartsWith("on 14d", audit.Reason);
    }

    [Fact]
    public async Task A_manager_of_another_branch_cannot_switch_it_on_and_nobody_can_on_an_admin()
    {
        using var w = new World();

        var elsewhere = await w.Panel(w.ManagerB, "Manager", At(10, 0)).Set(w.Worker, new(14));
        var onAdmin = await w.Panel(w.ManagerA, "Manager", At(10, 0)).Set(w.Admin, new(14));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(elsewhere).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(onAdmin).StatusCode);
        Assert.Null(w.Db.Employees.AsNoTracking().Single(e => e.Id == w.Worker).PresenceCheckUntilUtc);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(365)]
    public async Task Only_seven_fourteen_or_thirty_days(int days)
    {
        using var w = new World();

        var result = await w.Panel(w.Admin, "Admin", At(10, 0)).Set(w.Worker, new(days));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Switching_it_off_cancels_the_checks_already_drawn_for_today()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var planned = w.Planned(day, At(15, 0));

        await w.Panel(w.Admin, "Admin", At(12, 0)).Set(w.Worker, new(null));

        Assert.Null(w.Db.Employees.AsNoTracking().Single(e => e.Id == w.Worker).PresenceCheckUntilUtc);
        Assert.Equal(PresenceCheckResult.Cancelled, w.Reload(planned).Result);
        Assert.Equal(PresenceCheckRules.NoteDisabled, w.Reload(planned).Note);
    }

    [Fact]
    public async Task The_history_shows_what_was_asked_never_what_is_planned()
    {
        using var w = new World();
        w.SwitchOn(w.Worker);
        var day = w.Open(w.Worker, At(9, 2));
        var asked = w.Sent(day, At(11, 0));
        w.Planned(day, At(15, 0), slot: 1);
        await w.Phone(w.Worker, At(11, 2), new CapturingQueue()).Answer(asked.Id, new(FarLat, BranchLng, 10));

        var view = Json(await w.Panel(w.ManagerA, "Manager", At(12, 0)).Get(w.Worker));

        var checks = view.GetProperty("checks").EnumerateArray().ToList();
        var only = Assert.Single(checks);
        Assert.Equal("Outside", only.GetProperty("result").GetString());
        Assert.Equal(1, view.GetProperty("week").GetProperty("asked").GetInt32());
        Assert.Equal(1, view.GetProperty("week").GetProperty("outside").GetInt32());
        Assert.True(view.GetProperty("on").GetBoolean());
        Assert.True(view.GetProperty("canChange").GetBoolean());
    }

    // --- the world ----------------------------------------------------------------------------------------

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    private static ControllerContext Context(Guid who, string role) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", who.ToString()), new Claim("role", role)], "test")),
        },
    };

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FakeNotifier : IPushNotifier
    {
        public List<(Guid[] To, string Title, string Body, string? Url)> Sent { get; } = [];
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
        {
            Sent.Add((employeeIds.ToArray(), title, body, url));
            return Task.FromResult(employeeIds.Count);
        }
    }

    private sealed class CapturingQueue : IStaffAlertQueue
    {
        public List<StaffAlert> Items { get; } = [];
        public void Enqueue(StaffAlert alert) => Items.Add(alert);
        public ChannelReader<StaffAlert> Reader => Channel.CreateUnbounded<StaffAlert>().Reader;
    }

    private sealed class World : IDisposable
    {
        private readonly string _name = $"presence-{Guid.NewGuid()}";
        public AppDbContext Db { get; }
        public Guid SiteA { get; } = Guid.NewGuid();
        public Guid SiteB { get; } = Guid.NewGuid();
        public Guid Worker { get; } = Guid.NewGuid();
        public Guid Other { get; } = Guid.NewGuid();
        public Guid ManagerA { get; } = Guid.NewGuid();
        public Guid ManagerB { get; } = Guid.NewGuid();
        public Guid Admin { get; } = Guid.NewGuid();

        public World()
        {
            Db = ContextFor(TenantId);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = OtherTenantId, Name = "O", Slug = "o", DisplayName = "O", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = SiteA, TenantId = TenantId, Name = "Mərkəz", IsActive = true, QrVersion = 1, WorkDaysMask = 127,
                Latitude = BranchLat, Longitude = BranchLng, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            });
            Db.Locations.Add(new Location
            {
                Id = SiteB, TenantId = TenantId, Name = "Bazar qabağı", IsActive = true, QrVersion = 1, WorkDaysMask = 127,
                Latitude = 40.40, Longitude = 49.85, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            });
            Person(Worker, "Bağırov Zamiq", SiteA, EmployeeRole.Employee);
            Person(Other, "Həsənova Leyla", SiteA, EmployeeRole.Employee);
            Person(ManagerA, "Mərkəz Meneceri", SiteA, EmployeeRole.Manager);
            Person(ManagerB, "Bazar Meneceri", SiteB, EmployeeRole.Manager);
            Person(Admin, "Admin", SiteB, EmployeeRole.Admin);
            Db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantId, EmployeeId = ManagerA, LocationId = SiteA });
            Db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantId, EmployeeId = ManagerB, LocationId = SiteB });
            Db.SaveChanges();
        }

        public AppDbContext ContextFor(Guid tenantId)
        {
            var tenant = new TenantContext();
            tenant.Resolve(tenantId);
            return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options, tenant);
        }

        private void Person(Guid id, string name, Guid site, EmployeeRole role)
        {
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = name, Email = $"{id:N}@t.local", LocationId = site,
                Role = role, IsActive = true, PasswordHash = "x",
            });
            Db.SaveChanges();
        }

        public void SwitchOn(Guid id, DateTime? until = null)
        {
            var person = Db.Employees.Single(e => e.Id == id);
            person.PresenceCheckUntilUtc = until ?? At(9, 0, day: 25);
            Db.SaveChanges();
        }

        public AttendanceRecord Open(Guid id, DateTime checkInUtc)
        {
            var record = new AttendanceRecord
            {
                TenantId = TenantId, EmployeeId = id, LocationId = SiteA, AttendanceDate = Day,
                CheckInAtUtc = checkInUtc, Status = AttendanceStatus.OnTime,
            };
            Db.AttendanceRecords.Add(record);
            Db.SaveChanges();
            return record;
        }

        public void CheckOut(AttendanceRecord record, DateTime atUtc)
        {
            Db.AttendanceRecords.Single(r => r.Id == record.Id).CheckOutAtUtc = atUtc;
            Db.SaveChanges();
        }

        public PresenceCheck Planned(AttendanceRecord record, DateTime dueUtc, int slot = 0)
        {
            var check = new PresenceCheck
            {
                TenantId = TenantId, EmployeeId = record.EmployeeId, AttendanceRecordId = record.Id,
                LocationId = record.LocationId, CheckDate = record.AttendanceDate, Slot = slot, DueAtUtc = dueUtc,
            };
            Db.PresenceChecks.Add(check);
            Db.SaveChanges();
            return check;
        }

        public PresenceCheck Sent(AttendanceRecord record, DateTime sentUtc, int slot = 0)
        {
            var check = Planned(record, sentUtc, slot);
            check.SentAtUtc = sentUtc;
            check.Reached = 1;
            Db.SaveChanges();
            return check;
        }

        public PresenceCheck Reload(PresenceCheck check) =>
            Db.PresenceChecks.AsNoTracking().Single(c => c.Id == check.Id);

        public Task<PresenceSweepOutcome> Sweep(IPushNotifier push, DateTime nowUtc) =>
            PresenceCheckSweep.RunAsync(Db, push, pushConfigured: true, nowUtc, Baku, new Random(42));

        public PresenceChecksController Phone(Guid who, DateTime nowUtc, IStaffAlertQueue? alerts = null) =>
            new(Db, new FixedClock(nowUtc), alerts) { ControllerContext = Context(who, "Employee") };

        public AdminPresenceChecksController Panel(Guid who, string role, DateTime nowUtc) =>
            new(Db, new FixedClock(nowUtc)) { ControllerContext = Context(who, role) };

        public void Dispose() => Db.Dispose();
    }
}
