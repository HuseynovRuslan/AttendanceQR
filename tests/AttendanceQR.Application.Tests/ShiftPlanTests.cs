using System.Security.Claims;
using System.Text.Json;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Növbə planı»: who works which shift on which day. What is pinned here is the engine under the screen — that a
/// planned day, a rest day and an edited shift reach every rule through the one resolver, that a plan is only
/// written where the caller may plan, and that a day already worked is changed only on purpose.
/// </summary>
public class ShiftPlanTests
{
    private static readonly Guid Tenant = Guid.Parse("00000000-0000-0000-0000-0000000000d7");
    private static readonly DateOnly Today = DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku")));

    // ---- the resolver -------------------------------------------------------------------------------------------

    private static Schedule Shift(string name, int start, int end, int mask = 127) => new()
    {
        Name = name, ShiftStart = new TimeOnly(start, 0), ShiftEnd = new TimeOnly(end, 0),
        LateThresholdMinutes = 15, WorkDaysMask = mask,
    };

    private static Location Branch() => new()
    {
        Id = Guid.NewGuid(), Name = "Filial", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
        LateThresholdMinutes = 15, WorkDaysMask = 127,
    };

    [Fact]
    public void A_rest_day_keeps_the_persons_own_hours_on_a_day_that_is_not_a_working_day()
    {
        var day = Shift("Səhər", 6, 15);
        var rest = Shift("İstirahət", 9, 18, mask: 0);
        rest.IsRestDay = true;
        var schedules = new Dictionary<Guid, Schedule> { [day.Id] = day, [rest.Id] = rest };
        var date = new DateOnly(2026, 11, 18);

        var resolved = ShiftResolver.ScheduleOn(rest.Id, day.Id, date, schedules)!;
        var shift = EffectiveShift.Resolve(null, null, null, 1, null, resolved, Branch());

        Assert.False(shift.IsWorkingDay(date));                       // no «Qayıb», no reminder
        Assert.Equal((new TimeOnly(6, 0), new TimeOnly(15, 0)), shift.HoursOn(date)); // a scan that day still reads sensibly
        Assert.True(day.WorkDaysMask == 127);                          // the shift itself is untouched
    }

    [Fact]
    public void A_rest_day_for_somebody_with_no_shift_of_their_own_is_still_a_day_off()
    {
        var rest = Shift("İstirahət", 9, 18, mask: 0);
        rest.IsRestDay = true;
        var schedules = new Dictionary<Guid, Schedule> { [rest.Id] = rest };
        var date = new DateOnly(2026, 11, 18);

        var shift = EffectiveShift.Resolve(null, null, null, 1, null, ShiftResolver.ScheduleOn(rest.Id, null, date, schedules), Branch());

        Assert.False(shift.IsWorkingDay(date));
    }

    [Fact]
    public void An_edited_shift_answers_for_the_days_before_the_edit_as_it_was_then()
    {
        // The danger the screen was rebuilt around: 09:00–18:00 became 10:00–19:00 on the 12th, and a correction
        // to the 5th recomputed the 5th against the new hours — last month's pay, rewritten by next month's plan.
        var shift = Shift("Gündüz", 10, 19);
        var before = ShiftResolver.Snapshot(Shift("Gündüz", 9, 18));
        before.RevisionOfId = shift.Id;
        before.ValidUntil = new DateOnly(2026, 11, 11);
        var schedules = new Dictionary<Guid, Schedule> { [shift.Id] = shift, [before.Id] = before };

        Assert.Equal(new TimeOnly(9, 0), ShiftResolver.ScheduleOn(shift.Id, shift.Id, new DateOnly(2026, 11, 5), schedules)!.ShiftStart);
        Assert.Equal(new TimeOnly(9, 0), ShiftResolver.ScheduleOn(shift.Id, shift.Id, new DateOnly(2026, 11, 11), schedules)!.ShiftStart);
        Assert.Equal(new TimeOnly(10, 0), ShiftResolver.ScheduleOn(shift.Id, shift.Id, new DateOnly(2026, 11, 12), schedules)!.ShiftStart);
    }

    [Fact]
    public void Two_edits_keep_two_versions_each_for_its_own_days()
    {
        var shift = Shift("Gündüz", 11, 20);
        var first = Shift("Gündüz", 9, 18);
        first.RevisionOfId = shift.Id; first.ValidUntil = new DateOnly(2026, 10, 9);
        var second = Shift("Gündüz", 10, 19);
        second.RevisionOfId = shift.Id; second.ValidUntil = new DateOnly(2026, 10, 19);
        var schedules = new Dictionary<Guid, Schedule> { [shift.Id] = shift, [second.Id] = second, [first.Id] = first };

        Assert.Equal(9, ShiftResolver.ScheduleOn(shift.Id, null, new DateOnly(2026, 10, 1), schedules)!.ShiftStart.Hour);
        Assert.Equal(10, ShiftResolver.ScheduleOn(shift.Id, null, new DateOnly(2026, 10, 15), schedules)!.ShiftStart.Hour);
        Assert.Equal(11, ShiftResolver.ScheduleOn(shift.Id, null, new DateOnly(2026, 10, 25), schedules)!.ShiftStart.Hour);
    }

    [Fact]
    public void A_planned_night_reaches_the_override_map_and_crosses_midnight()
    {
        var day = Shift("Səhər", 6, 15);
        var night = Shift("Gecə", 20, 6);
        var schedules = new Dictionary<Guid, Schedule> { [day.Id] = day, [night.Id] = night };
        var person = Guid.NewGuid();
        var date = new DateOnly(2026, 11, 20);
        var map = new ShiftOverrideMap(new Dictionary<(Guid, DateOnly), Guid> { [(person, date)] = night.Id });

        var shift = EffectiveShift.Resolve(null, null, null, 1, null, map.ScheduleFor(person, date, day.Id, schedules), Branch());

        Assert.True(shift.CrossesIntoNextMorningOn(date));
        Assert.Equal(new TimeOnly(20, 0), shift.HoursOn(date).Start);
        Assert.False(EffectiveShift.Resolve(null, null, null, 1, null, map.ScheduleFor(person, date.AddDays(1), day.Id, schedules), Branch())
            .CrossesIntoNextMorningOn(date.AddDays(1)));
    }

    // ---- the plan API --------------------------------------------------------------------------------------------

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid BranchA { get; } = Guid.NewGuid();
        public Guid BranchB { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid WorkerA { get; } = Guid.NewGuid();
        public Guid WorkerA2 { get; } = Guid.NewGuid();
        public Guid WorkerB { get; } = Guid.NewGuid();
        public Schedule Day { get; }
        public Schedule Night { get; }
        public Schedule OtherBranchShift { get; }
        public List<DateOnly> Rebuilt { get; } = [];

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(Tenant);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"plan-{Guid.NewGuid()}").Options, tenant);
            Db.Tenants.Add(new Tenant { Id = Tenant, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(Location(BranchA, "Filial A"));
            Db.Locations.Add(Location(BranchB, "Filial B"));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = Tenant });
            Day = new Schedule { TenantId = Tenant, Name = "Səhər", ShiftStart = new(6, 0), ShiftEnd = new(15, 0), WorkDaysMask = 127, LateThresholdMinutes = 15 };
            Night = new Schedule { TenantId = Tenant, Name = "Gecə", ShiftStart = new(20, 0), ShiftEnd = new(6, 0), WorkDaysMask = 127, LateThresholdMinutes = 15 };
            OtherBranchShift = new Schedule { TenantId = Tenant, Name = "B növbəsi", LocationId = BranchB, ShiftStart = new(9, 0), ShiftEnd = new(18, 0), WorkDaysMask = 127, LateThresholdMinutes = 15 };
            Db.Schedules.AddRange(Day, Night, OtherBranchShift);
            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, BranchA, null));
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, BranchA, null));
            Db.Employees.Add(Person(WorkerA, "Aysel", EmployeeRole.Employee, BranchA, Day.Id));
            Db.Employees.Add(Person(WorkerA2, "Elçin", EmployeeRole.Employee, BranchA, Day.Id));
            Db.Employees.Add(Person(WorkerB, "Başqa filial", EmployeeRole.Employee, BranchB, Day.Id));
            Db.SaveChanges();
        }

        private static Location Location(Guid id, string name) => new()
        {
            Id = id, TenantId = Tenant, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId, Guid? scheduleId) => new()
        {
            Id = id, TenantId = Tenant, FullName = name, Role = role, LocationId = locationId, ScheduleId = scheduleId,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        private ClaimsPrincipal User(Guid id, EmployeeRole role) => new(new ClaimsIdentity(
            [new Claim("sub", id.ToString()), new Claim("role", role.ToString())], "test"));

        public ShiftPlanController Plan(Guid id, EmployeeRole role) => new(Db, new RecordingSummaries(Rebuilt))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(id, role) } },
        };

        public SchedulesController Templates() => new(Db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = User(AdminId, EmployeeRole.Admin) } },
        };

        public List<ShiftOverride> Rows(Guid employee) => Db.ShiftOverrides.AsNoTracking().Where(o => o.EmployeeId == employee).OrderBy(o => o.Date).ToList();
        public Guid RestId => Db.Schedules.AsNoTracking().Single(s => s.IsRestDay).Id;

        public void Dispose() => Db.Dispose();

        private sealed class RecordingSummaries(List<DateOnly> rebuilt) : IDailySummaryService
        {
            public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
            {
                rebuilt.Add(date);
                return Task.FromResult(0);
            }
        }
    }

    private static int Status(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => 0,
    };

    private static JsonElement Body(IActionResult result) =>
        JsonSerializer.SerializeToElement(((ObjectResult)result).Value);

    [Fact]
    public async Task A_rotation_is_written_day_by_day_from_its_first_day()
    {
        // «2 days, 2 nights, 2 off» for a week ahead: the six-step pattern, then it starts again.
        using var h = new Harness();
        var start = Today.AddDays(1);
        var result = await h.Plan(h.ManagerId, EmployeeRole.Manager).Apply(new ShiftPlanApplyRequest(
            [h.WorkerA], [h.Day.Id.ToString(), h.Day.Id.ToString(), h.Night.Id.ToString(), h.Night.Id.ToString(), "rest", "rest"],
            From: start, To: start.AddDays(6)));

        Assert.Equal(200, Status(result));
        var rows = h.Rows(h.WorkerA);
        Assert.Equal(7, rows.Count);
        var rest = h.RestId;
        Assert.Equal([h.Day.Id, h.Day.Id, h.Night.Id, h.Night.Id, rest, rest, h.Day.Id], rows.Select(r => r.ScheduleId));
        Assert.All(rows, r => Assert.Equal(h.ManagerId, r.CreatedByEmployeeId));
        Assert.Empty(h.Rebuilt); // nothing worked yet, nothing to rebuild
    }

    [Fact]
    public async Task One_rest_day_shift_is_made_for_the_company_and_reused()
    {
        using var h = new Harness();
        var plan = h.Plan(h.AdminId, EmployeeRole.Admin);
        await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA], ["rest"], Dates: [Today.AddDays(2)]));
        await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA2], ["rest"], Dates: [Today.AddDays(3)]));

        Assert.Single(h.Db.Schedules.AsNoTracking().Where(s => s.IsRestDay));
    }

    [Fact]
    public async Task Monday_to_Friday_makes_the_weekend_a_day_off()
    {
        using var h = new Harness();
        var monday = Today.AddDays(((int)DayOfWeek.Monday - (int)Today.DayOfWeek + 7) % 7 + 7);
        const int weekdays = 0b0111110;
        await h.Plan(h.AdminId, EmployeeRole.Admin).Apply(new ShiftPlanApplyRequest(
            [h.WorkerA], [h.Night.Id.ToString()], From: monday, To: monday.AddDays(6), Weekdays: weekdays));

        var rows = h.Rows(h.WorkerA);
        Assert.Equal(7, rows.Count);
        Assert.All(rows.Take(5), r => Assert.Equal(h.Night.Id, r.ScheduleId));
        Assert.All(rows.Skip(5), r => Assert.Equal(h.RestId, r.ScheduleId));
    }

    [Fact]
    public async Task A_manager_plans_their_own_branch_and_nobody_elses()
    {
        using var h = new Harness();
        var refused = await h.Plan(h.ManagerId, EmployeeRole.Manager).Apply(new ShiftPlanApplyRequest(
            [h.WorkerA, h.WorkerB], [h.Night.Id.ToString()], Dates: [Today.AddDays(1)]));

        Assert.Equal(403, Status(refused));
        Assert.Empty(h.Rows(h.WorkerA)); // all or nothing: the allowed half is not written either
        Assert.Empty(h.Rows(h.WorkerB));

        var staff = await h.Plan(h.WorkerA, EmployeeRole.Employee).Apply(new ShiftPlanApplyRequest(
            [h.WorkerA], ["rest"], Dates: [Today.AddDays(1)]));
        Assert.Equal(403, Status(staff)); // a worker may not retire their own absence
    }

    [Fact]
    public async Task A_branchs_own_shift_is_only_for_that_branch()
    {
        using var h = new Harness();
        var result = await h.Plan(h.AdminId, EmployeeRole.Admin).Apply(new ShiftPlanApplyRequest(
            [h.WorkerA], [h.OtherBranchShift.Id.ToString()], Dates: [Today.AddDays(1)]));

        Assert.Equal(400, Status(result));
        Assert.Empty(h.Rows(h.WorkerA));
    }

    [Fact]
    public async Task A_day_already_worked_changes_only_when_the_caller_says_so()
    {
        using var h = new Harness();
        var plan = h.Plan(h.AdminId, EmployeeRole.Admin);
        var days = new List<DateOnly> { Today.AddDays(-2), Today.AddDays(1) };

        var asked = await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA], [h.Night.Id.ToString()], Dates: days));
        Assert.Equal(409, Status(asked));
        Assert.Equal(1, Body(asked).GetProperty("pastDays").GetInt32());
        Assert.Empty(h.Rows(h.WorkerA));

        var confirmed = await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA], [h.Night.Id.ToString()], Dates: days, ConfirmPast: true));
        Assert.Equal(200, Status(confirmed));
        Assert.Equal(2, h.Rows(h.WorkerA).Count);
        Assert.Equal([Today.AddDays(-2)], h.Rebuilt); // the worked day is rebuilt at once; the day ahead is computed on read
    }

    [Fact]
    public async Task Clearing_a_day_gives_it_back_to_the_persons_own_shift()
    {
        using var h = new Harness();
        var plan = h.Plan(h.AdminId, EmployeeRole.Admin);
        var day = Today.AddDays(4);
        await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA, h.WorkerA2], [h.Night.Id.ToString()], Dates: [day]));
        var cleared = await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA], ["clear"], Dates: [day]));

        Assert.Equal(1, Body(cleared).GetProperty("cleared").GetInt32());
        Assert.Empty(h.Rows(h.WorkerA));
        Assert.Single(h.Rows(h.WorkerA2));
    }

    [Fact]
    public async Task Every_change_is_on_the_record_with_who_made_it_and_a_repeat_writes_nothing()
    {
        using var h = new Harness();
        var plan = h.Plan(h.ManagerId, EmployeeRole.Manager);
        var request = new ShiftPlanApplyRequest([h.WorkerA, h.WorkerA2], [h.Night.Id.ToString()], From: Today.AddDays(1), To: Today.AddDays(3));
        await plan.Apply(request);
        var again = await plan.Apply(request);

        var audit = h.Db.AuditLogs.AsNoTracking().Where(a => a.EventType == AuditEventType.ShiftPlanChanged).ToList();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => Assert.Contains(h.ManagerId.ToString(), a.Reason));
        Assert.All(audit, a => Assert.Contains("Gecə", a.Reason));
        Assert.Equal(6, Body(again).GetProperty("unchanged").GetInt32());
    }

    [Fact]
    public async Task The_grid_shows_the_plan_the_days_off_and_the_shifts_a_branch_can_use()
    {
        using var h = new Harness();
        var plan = h.Plan(h.ManagerId, EmployeeRole.Manager);
        var from = Today.AddDays(1);
        await plan.Apply(new ShiftPlanApplyRequest([h.WorkerA], [h.Night.Id.ToString(), "rest"], From: from, To: from.AddDays(1)));

        var grid = Body(await plan.Grid(h.BranchA, from, from.AddDays(2)));
        var aysel = grid.GetProperty("employees").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == h.WorkerA);
        var days = aysel.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal("plan", days[0].GetProperty("kind").GetString());
        Assert.Equal("20:00", days[0].GetProperty("start").GetString());
        Assert.True(days[0].GetProperty("overnight").GetBoolean());
        Assert.Equal("rest", days[1].GetProperty("kind").GetString());
        Assert.False(days[1].GetProperty("working").GetBoolean());
        Assert.Equal("base", days[2].GetProperty("kind").GetString());
        Assert.Equal("06:00", days[2].GetProperty("start").GetString());

        var shiftNames = grid.GetProperty("schedules").EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Səhər", "Gecə"], shiftNames); // no «İstirahət», no other branch's shift
        Assert.DoesNotContain(grid.GetProperty("employees").EnumerateArray(), e => e.GetProperty("id").GetGuid() == h.WorkerB);

        Assert.Equal(403, Status(await plan.Grid(h.BranchB, from, from)));
    }

    [Fact]
    public async Task A_manager_is_offered_only_their_branches()
    {
        using var h = new Harness();
        var names = Body(await h.Plan(h.ManagerId, EmployeeRole.Manager).Locations()).EnumerateArray()
            .Select(l => l.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Filial A"], names);
        Assert.Equal(2, Body(await h.Plan(h.AdminId, EmployeeRole.Admin).Locations()).GetArrayLength());
    }

    // ---- editing a shift keeps its history ----------------------------------------------------------------------

    private static ScheduleRequest Edit(Schedule s, string start, string end) =>
        new(s.Name, start, end, LocationId: s.LocationId, LateThresholdMinutes: s.LateThresholdMinutes, WorkDaysMask: s.WorkDaysMask);

    [Fact]
    public async Task Editing_a_shifts_hours_keeps_it_as_it_was_for_the_days_before()
    {
        using var h = new Harness();
        var day = h.Db.Schedules.Single(s => s.Id == h.Day.Id);
        day.CreatedAtUtc = DateTime.UtcNow.AddDays(-60);
        h.Db.SaveChanges();

        Assert.Equal(200, Status(await h.Templates().Update(h.Day.Id, Edit(h.Day, "07:00", "16:00"))));
        Assert.Equal(200, Status(await h.Templates().Update(h.Day.Id, Edit(h.Day, "08:00", "17:00")))); // twice the same day

        var revisions = h.Db.Schedules.AsNoTracking().Where(s => s.RevisionOfId == h.Day.Id).ToList();
        var revision = Assert.Single(revisions); // yesterday is kept once: the second edit changes only today on
        Assert.Equal(new TimeOnly(6, 0), revision.ShiftStart);
        Assert.Equal(Today.AddDays(-1), revision.ValidUntil);

        var catalogue = h.Db.Schedules.AsNoTracking().ToDictionary(s => s.Id);
        Assert.Equal(6, ShiftResolver.ScheduleOn(h.Day.Id, h.Day.Id, Today.AddDays(-5), catalogue)!.ShiftStart.Hour);
        Assert.Equal(8, ShiftResolver.ScheduleOn(h.Day.Id, h.Day.Id, Today, catalogue)!.ShiftStart.Hour);
    }

    [Fact]
    public async Task Renaming_a_shift_keeps_no_revision_and_revisions_are_never_listed()
    {
        using var h = new Harness();
        var day = h.Db.Schedules.Single(s => s.Id == h.Day.Id);
        day.CreatedAtUtc = DateTime.UtcNow.AddDays(-60);
        h.Db.SaveChanges();

        await h.Templates().Update(h.Day.Id, Edit(h.Day, "06:00", "15:00") with { Name = "Səhər növbəsi" });
        Assert.Empty(h.Db.Schedules.AsNoTracking().Where(s => s.RevisionOfId != null));

        await h.Templates().Update(h.Day.Id, Edit(h.Day, "07:00", "16:00"));
        await h.Plan(h.AdminId, EmployeeRole.Admin).Apply(new ShiftPlanApplyRequest([h.WorkerA], ["rest"], Dates: [Today.AddDays(1)]));
        var listed = Body(await h.Templates().List()).EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(3, listed.Count); // Səhər, Gecə, B növbəsi — not the revision, not «İstirahət»
    }

    [Fact]
    public async Task A_shift_still_in_somebodys_plan_cannot_be_deleted()
    {
        using var h = new Harness();
        await h.Plan(h.AdminId, EmployeeRole.Admin).Apply(new ShiftPlanApplyRequest([h.WorkerA], [h.Night.Id.ToString()], Dates: [Today.AddDays(3)]));

        var refused = await h.Templates().Delete(h.Night.Id);

        Assert.Equal(409, Status(refused));
        Assert.Equal("ScheduleInPlan", Body(refused).GetProperty("error").GetString());
    }
}
