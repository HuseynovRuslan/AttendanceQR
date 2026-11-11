using AttendanceQR.Api;
using AttendanceQR.Api.Approvals;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static AttendanceQR.Application.Tests.ApprovalWorld;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Where the approval flow is on (ApprovalArea), who stands in for an operator (ApproverSubstitution), and how the
/// approvers hear about requests (ApprovalNotifications): the owner's rules 1, 2, 5 and 6 of 2026-11-11.
/// </summary>
public class ApprovalAreaTests
{
    // --- switching an area on and off ---------------------------------------------------------------------

    [Fact]
    public async Task Only_the_company_administrator_switches_an_area_on_and_each_switch_is_audited()
    {
        using var a = await CreateAsync();
        var byOperator = await a.Areas().ActivateAsync(a.Operator1,
            new ApprovalAreaBody(AccessScopeType.Structure, a.S1, null), null, default);
        Assert.Equal(403, byOperator.Status);
        Assert.Equal("CompanyAdminOnly", Json(byOperator.Body).GetProperty("error").GetString());
        Assert.Empty(a.Db().ApprovalAreas.AsNoTracking());

        var id = await a.ActivateAsync(structure: a.S1);
        // Pressed twice: still one area.
        Assert.Equal(id, await a.ActivateAsync(structure: a.S1));
        Assert.Single(a.Db().ApprovalAreas.AsNoTracking());

        var audit = Assert.Single(a.Db().AuditLogs.AsNoTracking().Where(l => l.EventType == AuditEventType.ApprovalAreaChanged));
        Assert.Equal(a.W.Admin1, audit.EmployeeId);
        Assert.Contains("Aeroport yolu", audit.Reason);
    }

    [Fact]
    public async Task A_branch_with_no_structure_is_never_put_under_approval()
    {
        using var a = await CreateAsync();
        var result = await a.Areas().ActivateAsync(a.W.Admin1, new ApprovalAreaBody(AccessScopeType.Branch, null, a.W.B4), null, default);
        Assert.Equal(409, result.Status);
        Assert.Equal("BranchHasNoStructure", Json(result.Body).GetProperty("error").GetString());

        // Nor the whole company: there is no company-wide switch.
        var company = await a.Areas().ActivateAsync(a.W.Admin1, new ApprovalAreaBody(AccessScopeType.Company, null, null), null, default);
        Assert.Equal(400, company.Status);
        Assert.Empty(a.Db().ApprovalAreas.AsNoTracking());
    }

    [Fact]
    public async Task An_area_nobody_would_answer_for_is_not_switched_on()
    {
        using var a = await CreateAsync();
        await a.RevokeAsync(a.Operator2, AccessRoleTemplates.StructureOperator);
        var result = await a.Areas().ActivateAsync(a.W.Admin1, new ApprovalAreaBody(AccessScopeType.Structure, a.S2, null), null, default);
        Assert.Equal(409, result.Status);
        Assert.Equal("NoApproverInArea", Json(result.Body).GetProperty("error").GetString());

        // An operator who is no longer at work does not count either.
        using (var db = a.Db())
        {
            db.Employees.Single(e => e.Id == a.Operator1).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(409, (await a.Areas().ActivateAsync(a.W.Admin1, new ApprovalAreaBody(AccessScopeType.Structure, a.S1, null), null, default)).Status);
    }

    [Fact]
    public async Task A_structure_needs_its_own_structure_level_operator_while_a_branch_needs_one_at_the_branch()
    {
        using var a = await CreateAsync();
        // S2's operator is taken away, and somebody gets the operator role at B3 only.
        await a.RevokeAsync(a.Operator2, AccessRoleTemplates.StructureOperator);
        await a.GiveAsync(a.W.M2, AccessRoleTemplates.StructureOperator, branch: a.W.B3);

        var structure = await a.Areas().ActivateAsync(a.W.Admin1, new ApprovalAreaBody(AccessScopeType.Structure, a.S2, null), null, default);
        Assert.Equal(409, structure.Status);
        Assert.Equal("NoApproverInArea", Json(structure.Body).GetProperty("error").GetString());
        Assert.Equal(201, (await a.Areas().ActivateAsync(a.W.Admin1, new ApprovalAreaBody(AccessScopeType.Branch, null, a.W.B3), null, default)).Status);
    }

    [Fact]
    public async Task One_branch_of_a_structure_can_be_switched_on_alone()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(branch: a.W.B1);

        Assert.Equal(403, Read(await a.ManagerDesk(a.LegacyManager).CreateEmployee(DeskPerson(a.W.B1, "558000001"))).Status);
        // B2, the same structure but not switched on, works as today.
        Assert.Equal(200, Read(await a.ManagerDesk(a.LegacyManager).CreateEmployee(DeskPerson(a.W.B2, "558000002"))).Status);
    }

    [Fact]
    public async Task Switching_off_brings_back_the_direct_way_and_leaves_waiting_requests_to_be_decided()
    {
        using var a = await CreateAsync();
        var area = await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        Assert.Equal(200, (await a.Areas().DeactivateAsync(a.W.Admin1, area, null, default)).Status);
        Assert.NotNull(a.Db().ApprovalAreas.AsNoTracking().Single().DeactivatedAtUtc);

        Assert.Equal(200, Read(await a.ManagerDesk(a.LegacyManager).CreateEmployee(DeskPerson(a.W.B1, "559000001"))).Status);
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
        Assert.Single(a.PeopleWithPhone("551234567"));
        Assert.Equal(2, a.Db().AuditLogs.AsNoTracking().Count(l => l.EventType == AuditEventType.ApprovalAreaChanged));
    }

    [Fact]
    public async Task A_branch_moved_out_of_its_structure_leaves_the_area()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        using (var db = a.Db())
        {
            db.Locations.Single(l => l.Id == a.W.B2).StructureId = null;
            await db.SaveChangesAsync();
        }
        Assert.Equal(200, Read(await a.ManagerDesk(a.LegacyManager).CreateEmployee(DeskPerson(a.W.B2, "559000002"))).Status);
        Assert.Equal(409, (await a.Desk().CreateEmployeeAsync(a.LegacyManager, Person(a.W.B2, "559000003"), null, default)).Status);
    }

    [Fact]
    public async Task Me_tells_each_person_what_they_do_where_requests_are_on()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default);

        var keeper = Json((await a.Desk().MeAsync(a.Timekeeper, default)).Body);
        var b1 = Assert.Single(keeper.GetProperty("branches").EnumerateArray());
        Assert.True(b1.GetProperty("canRequest").GetBoolean());
        Assert.False(b1.GetProperty("canActDirectly").GetBoolean());
        Assert.False(b1.GetProperty("isApprover").GetBoolean());

        var op = Json((await a.Desk().MeAsync(a.Operator1, default)).Body);
        Assert.Equal(2, op.GetProperty("branches").GetArrayLength());
        Assert.Equal(1, op.GetProperty("queueCount").GetInt32());
        Assert.All(op.GetProperty("branches").EnumerateArray(), b => Assert.True(b.GetProperty("isApprover").GetBoolean()));
    }

    [Fact]
    public async Task Me_names_the_structure_and_shows_an_observer_their_branches_read_only()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);

        var viewer = Json((await a.Desk().MeAsync(a.Observer, default)).Body).GetProperty("branches").EnumerateArray().ToList();
        Assert.Equal(2, viewer.Count);
        Assert.All(viewer, b =>
        {
            Assert.True(b.GetProperty("canView").GetBoolean());
            Assert.False(b.GetProperty("canRequest").GetBoolean());
            Assert.False(b.GetProperty("isApprover").GetBoolean());
            Assert.False(b.GetProperty("canActDirectly").GetBoolean());
            Assert.Equal("Aeroport yolu", b.GetProperty("structureName").GetString());
        });
        // Plain staff of the branch: nothing at all.
        Assert.Equal(0, Json((await a.Desk().MeAsync(a.W.E2, default)).Body).GetProperty("branches").GetArrayLength());
    }

    [Fact]
    public async Task The_new_person_form_offers_the_catalogue_and_the_branchs_shifts_to_who_may_ask()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        using (var db = a.Db())
        {
            db.JobPositions.Add(new JobPosition { TenantId = AccessWorld.TenantA, Name = "Bağban" });
            db.Schedules.Add(new Schedule { TenantId = AccessWorld.TenantA, Name = "B1 səhər", LocationId = a.W.B1, ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(17, 0) });
            db.Schedules.Add(new Schedule { TenantId = AccessWorld.TenantA, Name = "Şirkət", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0) });
            db.Schedules.Add(new Schedule { TenantId = AccessWorld.TenantA, Name = "B3 axşam", LocationId = a.W.B3, ShiftStart = new TimeOnly(14, 0), ShiftEnd = new TimeOnly(23, 0) });
            await db.SaveChangesAsync();
        }

        var options = await a.Desk().OptionsAsync(a.Timekeeper, a.W.B1, default);
        Assert.Equal(200, options.Status);
        var body = Json(options.Body);
        Assert.Equal(["Bağban"], body.GetProperty("positions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(["B1 səhər", "Şirkət"], body.GetProperty("schedules").EnumerateArray().Select(s => s.GetProperty("name").GetString()));

        // Not at a branch they do not ask for, not for plain staff, not for another company's branch.
        Assert.Equal(403, (await a.Desk().OptionsAsync(a.Timekeeper, a.W.B2, default)).Status);
        Assert.Equal(403, (await a.Desk().OptionsAsync(a.W.E2, a.W.B2, default)).Status);
        Assert.Equal(404, (await a.Desk().OptionsAsync(a.Timekeeper, a.W.OtherBranch, default)).Status);
    }

    // --- an Operator standing in for another structure ---------------------------------------------------

    /// <summary>Operator2 (S2's own operator) is named by the Company administrator to decide S1's requests.</summary>
    private static async Task<Guid> StandInAsync(ApprovalWorld a, Guid operatorId, Guid structure, TimeSpan length)
    {
        var result = await a.Areas().CreateSubstitutionAsync(a.W.Admin2,
            new SubstitutionBody(structure, operatorId, null, a.Clock.Now.UtcDateTime + length, "Rasim məzuniyyətdədir"), null, default);
        Assert.True(result.Status == 201, $"stand-in answered {result.Status}: {Json(result.Body)}");
        return Json(result.Body).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Another_structures_operator_stands_in_and_the_stand_in_ends_by_itself()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var first = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000011"), null, default));
        var second = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000012"), null, default));

        // Before: S1's requests are not theirs.
        Assert.Equal(404, (await a.Desk().ApproveAsync(a.Operator2, first, null, default)).Status);

        await StandInAsync(a, a.Operator2, a.S1, TimeSpan.FromDays(3));
        Assert.Single(a.Db().AuditLogs.AsNoTracking().Where(l => l.EventType == AuditEventType.ApproverSubstitutionChanged));

        // They see the queue and decide.
        var me = Json((await a.Desk().MeAsync(a.Operator2, default)).Body);
        Assert.Equal(2, me.GetProperty("queueCount").GetInt32());
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator2, first, null, default)).Status);

        // Three days later it is over, with nobody having to remember to end it.
        a.Clock.Now = a.Clock.Now.AddDays(3).AddMinutes(1);
        Assert.Equal(404, (await a.Desk().ApproveAsync(a.Operator2, second, null, default)).Status);
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, second, null, default)).Status);
    }

    [Fact]
    public async Task A_stand_in_decides_and_reads_requests_and_nothing_more()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        await StandInAsync(a, a.Operator2, a.S1, TimeSpan.FromDays(5));

        // No direct change at the structure's branch, and no request of their own there.
        using (var db = a.Db())
            Assert.Equal(ApprovalGate.Outcome.Refused,
                (await ApprovalGate.DirectChangeAsync(db, a.On, a.Operator2, a.W.B1, default)).Outcome);
        Assert.Equal(403, (await a.Desk().CreateEmployeeAsync(a.Operator2, Person(a.W.B1, "551000031"), null, default)).Status);

        // Their own request at a branch of the structure (M5 is today's manager of B2) stays somebody else's.
        var own = IdOf(await a.Desk().CreateEmployeeAsync(a.Operator2, Person(a.W.B2, "551000032"), null, default));
        var self = await a.Desk().ApproveAsync(a.Operator2, own, null, default);
        Assert.Equal(403, self.Status);
        Assert.Equal("SelfDecisionNotAllowed", Json(self.Body).GetProperty("error").GetString());
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, own, null, default)).Status);

        // A stand-in reaches one structure, the one it was given for. S2's requests are still not S1's operator's to
        // decide (M4 sees them as today's manager of B3, and is told so).
        await a.ActivateAsync(structure: a.S2);
        var s2 = IdOf(await a.Desk().CreateEmployeeAsync(a.Operator2, Person(a.W.B3, "551000033"), null, default));
        var other = await a.Desk().ApproveAsync(a.Operator1, s2, null, default);
        Assert.Equal(403, other.Status);
        Assert.Equal("NotApprover", Json(other.Body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Only_an_existing_operator_is_named_and_only_for_a_while()
    {
        using var a = await CreateAsync();
        var until = a.Clock.Now.UtcDateTime.AddDays(5);
        async Task<(int Status, string? Error)> Name(Guid caller, Guid who, Guid structure, DateTime ends)
        {
            var r = await a.Areas().CreateSubstitutionAsync(caller, new SubstitutionBody(structure, who, null, ends, null), null, default);
            return (r.Status, Json(r.Body).TryGetProperty("error", out var e) ? e.GetString() : null);
        }

        // An observer, a timekeeper or a Company administrator who decides nowhere is never made an approver.
        Assert.Equal((409, "SubstituteMustBeOperator"), await Name(a.W.Admin1, a.Observer, a.S1, until));
        Assert.Equal((409, "SubstituteMustBeOperator"), await Name(a.W.Admin1, a.Timekeeper, a.S1, until));
        Assert.Equal((409, "SubstituteMustBeOperator"), await Name(a.W.Admin1, a.W.Admin2, a.S1, until));
        // S1's own operator already decides S1.
        Assert.Equal((409, "AlreadyApproverOfStructure"), await Name(a.W.Admin1, a.Operator1, a.S1, until));
        // An operator does not name stand-ins; the Company administrator does.
        Assert.Equal((403, "CompanyAdminOnly"), await Name(a.Operator1, a.Operator2, a.S1, until));
        // Longer than 31 days is a new operator, not a stand-in; and it must end in the future.
        Assert.Equal((400, "SubstitutionTooLong"), await Name(a.W.Admin1, a.Operator2, a.S1, a.Clock.Now.UtcDateTime.AddDays(32)));
        Assert.Equal((400, "InvalidPeriod"), await Name(a.W.Admin1, a.Operator2, a.S1, a.Clock.Now.UtcDateTime.AddHours(-1)));
        Assert.Empty(a.Db().ApproverSubstitutions.AsNoTracking());

        // Exactly 31 days is allowed; a second one over the same days is refused.
        Assert.Equal(201, (await Name(a.W.Admin1, a.Operator2, a.S1, a.Clock.Now.UtcDateTime.AddDays(31))).Status);
        Assert.Equal((409, "SubstitutionOverlaps"), await Name(a.W.Admin1, a.Operator2, a.S1, until));
    }

    [Fact]
    public async Task A_stand_in_ended_early_or_whose_holder_stopped_being_an_operator_decides_nothing()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        var standIn = await StandInAsync(a, a.Operator2, a.S1, TimeSpan.FromDays(2));
        Assert.Equal(200, (await a.Areas().RevokeSubstitutionAsync(a.W.Admin2, standIn, null, default)).Status);
        Assert.Equal(404, (await a.Desk().ApproveAsync(a.Operator2, id, null, default)).Status);

        await StandInAsync(a, a.Operator2, a.S1, TimeSpan.FromDays(2));
        await a.RevokeAsync(a.Operator2, AccessRoleTemplates.StructureOperator);
        Assert.Equal(404, (await a.Desk().ApproveAsync(a.Operator2, id, null, default)).Status);
        Assert.Equal(ChangeRequestStatus.Pending, a.Request(id).Status);
    }

    [Fact]
    public async Task A_stand_in_hears_about_the_structures_new_requests()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        await StandInAsync(a, a.Operator2, a.S1, TimeSpan.FromDays(2));
        await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default);
        var push = new Recorder();

        await SweepAsync(a, push, a.Clock.Now.UtcDateTime);
        Assert.Equal(new[] { a.Operator1, a.Operator2 }.Order(), Assert.Single(push.Sent).To.Order());
    }

    // --- telling the approvers ----------------------------------------------------------------------------

    private sealed class Recorder : IPushNotifier
    {
        public List<(Guid[] To, string Title, string Body)> Sent { get; } = [];
        public bool Fail { get; set; }

        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> ids, string title, string body, string? url, CancellationToken ct = default)
        {
            if (Fail)
                throw new HttpRequestException("push service down");
            Sent.Add((ids.ToArray(), title, body));
            return Task.FromResult(ids.Count);
        }
    }

    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private static Task<ApprovalNotifyOutcome> SweepAsync(ApprovalWorld a, Recorder push, DateTime nowUtc) =>
        ApprovalNotifications.RunAsync(a.Db(), push, pushOn: true, nowUtc, Baku, NullLogger.Instance, default);

    [Fact]
    public async Task A_new_request_is_announced_once_to_its_approvers_and_nobody_else()
    {
        using var a = await CreateAsync();
        await a.GiveAsync(a.W.M2, AccessRoleTemplates.StructureOperator, structure: a.S1);
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));
        var push = new Recorder();

        var outcome = await SweepAsync(a, push, a.Clock.Now.UtcDateTime);
        Assert.Equal(1, outcome.Announced);
        var sent = Assert.Single(push.Sent);
        Assert.Equal(new[] { a.Operator1, a.W.M2 }.Order(), sent.To.Order());   // not the admin, the observer or the asker
        Assert.Contains("Aysel Məmmədova", sent.Body);
        Assert.NotNull(a.Request(id).NotifiedAtUtc);

        await SweepAsync(a, push, a.Clock.Now.UtcDateTime.AddMinutes(1));
        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task A_push_that_fails_costs_a_banner_and_never_the_request()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        var outcome = await SweepAsync(a, new Recorder { Fail = true }, a.Clock.Now.UtcDateTime);
        Assert.Equal(1, outcome.Announced);
        var request = a.Request(id);
        Assert.Equal(ChangeRequestStatus.Pending, request.Status);
        Assert.NotNull(request.NotifiedAtUtc);
        // And the request is decided exactly as if the push had gone out.
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
    }

    [Fact]
    public async Task Requests_waiting_more_than_a_day_are_recalled_once_a_day_in_the_daytime()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000021"), null, default);
        await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000022"), null, default);
        var push = new Recorder();
        await SweepAsync(a, push, a.Clock.Now.UtcDateTime);          // the two announcements
        push.Sent.Clear();

        // The next day, 09:00 in Baku: more than 24 hours, but not before ten.
        var nextMorning = new DateTime(2026, 11, 12, 5, 0, 0, DateTimeKind.Utc);
        Assert.Equal(0, (await SweepAsync(a, push, nextMorning)).Reminded);
        Assert.Empty(push.Sent);

        // 12:01 in Baku: one message per approver, with how many wait.
        var noon = new DateTime(2026, 11, 12, 8, 1, 0, DateTimeKind.Utc);
        Assert.Equal(2, (await SweepAsync(a, push, noon)).Reminded);
        var reminder = Assert.Single(push.Sent);
        Assert.Equal([a.Operator1], reminder.To);
        Assert.Equal("24 saatdan çox gözləyən müraciət: 2", reminder.Body);

        // Later the same day: nothing more.
        await SweepAsync(a, push, noon.AddHours(3));
        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task A_decided_request_is_neither_announced_nor_recalled()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));
        Assert.Equal(200, (await a.Desk().RejectAsync(a.Operator1, id, "Artıq lazım deyil", default)).Status);
        var push = new Recorder();

        await SweepAsync(a, push, a.Clock.Now.UtcDateTime.AddDays(2));
        Assert.Empty(push.Sent);
    }

    // --- the sessions that may only look ------------------------------------------------------------------

    [Theory]
    [InlineData("/api/approvals/requests/employee-create")]
    [InlineData("/api/approvals/requests/00000000-0000-0000-0000-000000000001/approve")]
    [InlineData("/api/approvals/areas")]
    [InlineData("/api/approvals/substitutions")]
    public void A_support_or_view_only_session_reads_but_never_writes_here(string path)
    {
        Assert.False(ImpersonationBoundary.IsAllowed("POST", new PathString(path)));
        Assert.False(ViewOnlyBoundary.IsAllowed("POST", new PathString(path)));
        Assert.True(ImpersonationBoundary.IsAllowed("GET", new PathString(path)));
        Assert.True(ViewOnlyBoundary.IsAllowed("GET", new PathString(path)));
    }
}
