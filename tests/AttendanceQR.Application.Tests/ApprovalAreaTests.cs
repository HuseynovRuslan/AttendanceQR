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

    // --- a Company administrator standing in --------------------------------------------------------------

    [Fact]
    public async Task A_stand_in_lets_a_company_administrator_decide_until_it_ends_by_itself()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var first = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000011"), null, default));
        var second = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000012"), null, default));

        var standIn = await a.Areas().CreateSubstitutionAsync(a.W.Admin2,
            new SubstitutionBody(a.S1, a.W.Admin1, null, a.Clock.Now.UtcDateTime.AddDays(3), "Rasim məzuniyyətdədir"), null, default);
        Assert.Equal(201, standIn.Status);
        Assert.Single(a.Db().AuditLogs.AsNoTracking().Where(l => l.EventType == AuditEventType.ApproverSubstitutionChanged));

        Assert.Equal(200, (await a.Desk().ApproveAsync(a.W.Admin1, first, null, default)).Status);
        // Only for the structure it was given for: S2 is not theirs to decide.
        await a.ActivateAsync(structure: a.S2);
        var s2 = IdOf(await a.Desk().CreateEmployeeAsync(a.Operator2, Person(a.W.B3, "551000013"), null, default));
        Assert.Equal(403, (await a.Desk().ApproveAsync(a.W.Admin1, s2, null, default)).Status);

        // Three days later it is over, with nobody having to remember to end it.
        a.Clock.Now = a.Clock.Now.AddDays(3).AddMinutes(1);
        Assert.Equal(403, (await a.Desk().ApproveAsync(a.W.Admin1, second, null, default)).Status);
    }

    [Fact]
    public async Task Only_a_company_administrator_stands_in_and_only_for_a_while()
    {
        using var a = await CreateAsync();
        var until = a.Clock.Now.UtcDateTime.AddDays(5);

        // An observer is never made an approver, this way or any other.
        var observer = await a.Areas().CreateSubstitutionAsync(a.W.Admin1, new SubstitutionBody(a.S1, a.Observer, null, until, null), null, default);
        Assert.Equal(409, observer.Status);
        Assert.Equal("SubstituteMustBeCompanyAdmin", Json(observer.Body).GetProperty("error").GetString());
        // Nor does an operator hand out stand-ins.
        Assert.Equal(403, (await a.Areas().CreateSubstitutionAsync(a.Operator1, new SubstitutionBody(a.S1, a.W.Admin1, null, until, null), null, default)).Status);
        // Longer than a month is a new operator, not a stand-in; and it must end in the future.
        Assert.Equal(400, (await a.Areas().CreateSubstitutionAsync(a.W.Admin1,
            new SubstitutionBody(a.S1, a.W.Admin1, null, a.Clock.Now.UtcDateTime.AddDays(32), null), null, default)).Status);
        Assert.Equal(400, (await a.Areas().CreateSubstitutionAsync(a.W.Admin1,
            new SubstitutionBody(a.S1, a.W.Admin1, null, a.Clock.Now.UtcDateTime.AddHours(-1), null), null, default)).Status);
        Assert.Empty(a.Db().ApproverSubstitutions.AsNoTracking());
    }

    [Fact]
    public async Task A_stand_in_ended_early_decides_nothing_more()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));
        var standIn = Json((await a.Areas().CreateSubstitutionAsync(a.W.Admin2,
            new SubstitutionBody(a.S1, a.W.Admin1, null, a.Clock.Now.UtcDateTime.AddDays(2), null), null, default)).Body).GetProperty("id").GetGuid();

        Assert.Equal(200, (await a.Areas().RevokeSubstitutionAsync(a.W.Admin2, standIn, null, default)).Status);
        Assert.Equal(403, (await a.Desk().ApproveAsync(a.W.Admin1, id, null, default)).Status);
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
