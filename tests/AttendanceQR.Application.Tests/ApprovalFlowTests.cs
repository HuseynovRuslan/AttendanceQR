using System.Text.Json;
using AttendanceQR.Api.Approvals;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static AttendanceQR.Application.Tests.ApprovalWorld;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Yeni işçi» from the branch to the account: Timekeeper → Pending → Operator → Approved or Rejected → the
/// account → the history. These tests pin the promises of ChangeRequestDesk and ApprovalGate in every AuthzMode,
/// with the branch and company walls intact.
/// </summary>
public class ApprovalFlowTests
{
    // --- the whole flow ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_timekeepers_request_waits_for_the_operator_and_the_approval_creates_the_account()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);

        var created = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), "press-1", default);
        Assert.Equal(201, created.Status);
        var id = IdOf(created);
        Assert.Equal(ChangeRequestStatus.Pending, a.Request(id).Status);
        Assert.Empty(a.PeopleWithPhone("551234567"));                     // nothing exists until somebody decides

        // The operator's queue has it; the timekeeper's «mine» has it; nobody else had to do anything.
        var queue = Json((await a.Desk().ListAsync(a.Operator1, "queue", null, null, null, default)).Body);
        var row = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(id, row.GetProperty("id").GetGuid());
        Assert.True(row.GetProperty("canDecide").GetBoolean());
        Assert.Equal("Aysel Məmmədova", row.GetProperty("subject").GetString());
        var mine = Json((await a.Desk().ListAsync(a.Timekeeper, "mine", null, null, null, default)).Body);
        Assert.True(Assert.Single(mine.GetProperty("items").EnumerateArray()).GetProperty("canCancel").GetBoolean());

        var approved = await a.Desk().ApproveAsync(a.Operator1, id, "Sənədlər yoxlandı", default);
        Assert.Equal(200, approved.Status);
        var body = Json(approved.Body);
        Assert.Matches("^[0-9]{4}$", body.GetProperty("tempPin").GetString());

        var person = Assert.Single(a.PeopleWithPhone("551234567"));
        Assert.Equal(body.GetProperty("employeeId").GetGuid(), person.Id);
        Assert.Equal(a.W.B1, person.LocationId);
        Assert.Equal(EmployeeRole.Employee, person.Role);
        Assert.True(person.MustChangePin);
        Assert.Equal("Aysel Məmmədova", person.FullName);
        // A request carries no check-in exceptions: the new person starts on the branch's own rules.
        Assert.False(person.CanFieldCheckIn);
        Assert.False(person.PhotoExempt);
        Assert.Null(person.QrlessCheckInOverride);
        Assert.Null(person.RequireGeofenceOverride);

        var request = a.Request(id);
        Assert.Equal(ChangeRequestStatus.Approved, request.Status);
        Assert.Equal(person.Id, request.EmployeeId);
        Assert.Equal(a.Operator1, request.DecidedByEmployeeId);
        Assert.NotNull(request.AppliedAtUtc);
        Assert.Equal("Sənədlər yoxlandı", request.DecisionNote);
        Assert.Equal(
            [ChangeRequestEventKind.Created, ChangeRequestEventKind.Approved, ChangeRequestEventKind.Applied],
            a.History(id));

        // The detail tells the story the history holds, and offers nobody a second decision.
        var detail = Json((await a.Desk().DetailAsync(a.Operator1, id, default)).Body);
        Assert.Equal(3, detail.GetProperty("history").GetArrayLength());
        Assert.False(detail.GetProperty("request").GetProperty("canDecide").GetBoolean());
    }

    [Theory]
    [InlineData(AuthzMode.Legacy)]
    [InlineData(AuthzMode.Shadow)]
    [InlineData(AuthzMode.Enforced)]
    public async Task In_an_area_todays_manager_cannot_add_directly_in_any_mode_and_is_offered_the_request(AuthzMode mode)
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        await a.SetModeAsync(mode);

        var direct = Read(await a.ManagerDesk(a.LegacyManager).CreateEmployee(DeskPerson(a.W.B1, "551111111")));
        Assert.Equal(403, direct.Status);
        Assert.Equal("ApprovalRequired", direct.Body.GetProperty("error").GetString());
        Assert.True(direct.Body.GetProperty("canRequest").GetBoolean());
        Assert.Empty(a.PeopleWithPhone("551111111"));

        // The same person, asked for instead: accepted and waiting.
        var asked = await a.Desk().CreateEmployeeAsync(a.LegacyManager, Person(a.W.B1, "551111111"), null, default);
        Assert.Equal(201, asked.Status);
    }

    [Theory]
    [InlineData(AuthzMode.Legacy)]
    [InlineData(AuthzMode.Shadow)]
    [InlineData(AuthzMode.Enforced)]
    public async Task The_operator_adds_directly_and_the_addition_is_recorded_in_the_area(AuthzMode mode)
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        await a.SetModeAsync(mode);

        var result = Read(await a.ManagerDesk(a.Operator1).CreateEmployee(DeskPerson(a.W.B2, "552222222")));
        Assert.Equal(200, result.Status);
        var person = Assert.Single(a.PeopleWithPhone("552222222"));

        var record = Assert.Single(a.Db().ChangeRequests.AsNoTracking().Where(r => r.Direct));
        Assert.Equal(ChangeRequestStatus.Approved, record.Status);
        Assert.Equal(person.Id, record.EmployeeId);
        Assert.Equal(a.Operator1, record.RequestedByEmployeeId);
        Assert.Equal(a.Operator1, record.DecidedByEmployeeId);
        Assert.Equal(a.S1, record.StructureIdAtCreation);
        Assert.Null(record.Number);
        Assert.Equal([ChangeRequestEventKind.Direct], a.History(record.Id));
    }

    [Fact]
    public async Task Outside_every_area_nothing_changes_and_nothing_is_recorded()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);

        // B4 is in no structure: M4 manages it and adds people as today.
        Assert.Equal(200, Read(await a.ManagerDesk(a.W.M4).CreateEmployee(DeskPerson(a.W.B4, "553333333"))).Status);
        // B3 is in S2, which nobody switched on.
        Assert.Equal(200, Read(await a.ManagerDesk(a.W.M5).CreateEmployee(DeskPerson(a.W.B3, "553333334"))).Status);
        Assert.Empty(a.Db().ChangeRequests.AsNoTracking());

        // And no request can be made there: the branch is on today's flow.
        var b4 = await a.Desk().CreateEmployeeAsync(a.W.M4, Person(a.W.B4, "553333335"), null, default);
        Assert.Equal(409, b4.Status);
        Assert.Equal("NotInApprovalArea", Json(b4.Body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task With_the_switch_off_an_area_row_does_nothing()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var off = new ApprovalOptions { Enabled = false };

        Assert.Equal(200, Read(await a.ManagerDesk(a.LegacyManager, off).CreateEmployee(DeskPerson(a.W.B1, "554444444"))).Status);
        Assert.Empty(a.Db().ChangeRequests.AsNoTracking());

        var api = a.Api(a.Timekeeper, off);
        Assert.Equal(404, Read(await api.CreateEmployee(Person(a.W.B1, "554444445"), null)).Status);
        Assert.Equal(404, Read(await api.List(null, null, null, null)).Status);
        Assert.Equal(404, Read(await api.Approve(Guid.NewGuid(), null)).Status);
        var me = Read(await api.Me());
        Assert.Equal(200, me.Status);
        Assert.False(me.Body.GetProperty("enabled").GetBoolean());
    }

    // --- who may do what ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_timekeeper_asks_only_for_their_own_branch()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);

        var other = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B2), null, default);
        Assert.Equal(403, other.Status);
        Assert.Equal("RequestNotAllowed", Json(other.Body).GetProperty("error").GetString());
        // Plain staff at the branch hold no request right at all.
        Assert.Equal(403, (await a.Desk().CreateEmployeeAsync(a.W.E2, Person(a.W.B2), null, default)).Status);
    }

    [Fact]
    public async Task Another_structures_operator_neither_sees_nor_decides()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        Assert.Equal(404, (await a.Desk().DetailAsync(a.Operator2, id, default)).Status);
        Assert.Equal(404, (await a.Desk().ApproveAsync(a.Operator2, id, null, default)).Status);
        Assert.Equal(404, (await a.Desk().RejectAsync(a.Operator2, id, "Bizim filial deyil", default)).Status);
        var queue = Json((await a.Desk().ListAsync(a.Operator2, "queue", null, null, null, default)).Body);
        Assert.Equal(0, queue.GetProperty("items").GetArrayLength());
        Assert.Equal(ChangeRequestStatus.Pending, a.Request(id).Status);
    }

    [Fact]
    public async Task The_observer_sees_the_request_and_is_never_offered_or_allowed_a_decision()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        var detail = Json((await a.Desk().DetailAsync(a.Observer, id, default)).Body);
        Assert.False(detail.GetProperty("request").GetProperty("canDecide").GetBoolean());
        Assert.Equal(403, (await a.Desk().ApproveAsync(a.Observer, id, null, default)).Status);
        Assert.Equal(403, (await a.Desk().RejectAsync(a.Observer, id, "Mən qərar verə bilmərəm", default)).Status);
        Assert.Equal(403, (await a.Desk().CreateEmployeeAsync(a.Observer, Person(a.W.B1, "555555551"), null, default)).Status);
        Assert.Equal(ChangeRequestStatus.Pending, a.Request(id).Status);
    }

    [Fact]
    public async Task Nobody_decides_their_own_request_not_even_an_operator()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        // The operator may ask too, and then it is another approver's to decide.
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Operator1, Person(a.W.B1), null, default));

        var own = await a.Desk().ApproveAsync(a.Operator1, id, null, default);
        Assert.Equal(403, own.Status);
        Assert.Equal("SelfDecisionNotAllowed", Json(own.Body).GetProperty("error").GetString());
        Assert.Equal(403, (await a.Desk().RejectAsync(a.Operator1, id, "Özümü rədd edirəm", default)).Status);

        await a.GiveAsync(a.W.M2, AccessRoleTemplates.StructureOperator, structure: a.S1);
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.W.M2, id, null, default)).Status);
    }

    [Fact]
    public async Task The_company_administrator_is_not_the_daily_approver()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        // Sees everything, decides nothing, unless standing in (ApprovalAreaTests).
        Assert.Equal(200, (await a.Desk().DetailAsync(a.W.Admin1, id, default)).Status);
        var approve = await a.Desk().ApproveAsync(a.W.Admin1, id, null, default);
        Assert.Equal(403, approve.Status);
        Assert.Equal("NotApprover", Json(approve.Body).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Rights_are_read_again_at_the_moment_of_the_decision()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        await a.RevokeAsync(a.Operator1, AccessRoleTemplates.StructureOperator);
        Assert.Equal(403, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
        Assert.Empty(a.PeopleWithPhone("551234567"));

        // And at the moment of asking: a timekeeper whose role is gone asks nothing.
        await a.RevokeAsync(a.Timekeeper, AccessRoleTemplates.Timekeeper);
        Assert.Equal(403, (await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551234568"), null, default)).Status);
    }

    [Fact]
    public async Task Another_companys_requests_and_branches_do_not_exist_here()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        // From company B: the request is not there to read, list or decide.
        var b = a.Desk(tenant: AccessWorld.TenantB);
        Assert.Equal(404, (await b.DetailAsync(a.W.OtherCompanyManager, id, default)).Status);
        Assert.Equal(404, (await a.Desk(tenant: AccessWorld.TenantB).ApproveAsync(a.W.OtherCompanyManager, id, null, default)).Status);
        var list = Json((await a.Desk(tenant: AccessWorld.TenantB).ListAsync(a.W.OtherCompanyManager, "all", null, null, null, default)).Body);
        Assert.Equal(0, list.GetProperty("items").GetArrayLength());

        // From company A: company B's branch is not a branch.
        var foreign = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.OtherBranch, "556666666"), null, default);
        Assert.Equal(404, foreign.Status);
        Assert.Equal("LocationNotFound", Json(foreign.Body).GetProperty("error").GetString());
    }

    // --- what a reader of a request may see -------------------------------------------------------------

    private static EmployeeCreateRequestBody PersonWithDetails(Guid branch) =>
        new("Aysel", "Məmmədova", "Rəşid", "551234567", "aysel@example.az", null, new DateOnly(1994, 5, 17), branch, null, "Yeni işçi");

    private static async Task<JsonElement> PayloadAs(ApprovalWorld a, Guid caller, Guid id) =>
        Json((await a.Desk().DetailAsync(caller, id, default)).Body);

    [Fact]
    public async Task An_observer_reads_the_request_but_never_the_persons_phone_email_or_birth_date()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, PersonWithDetails(a.W.B1), null, default));

        var observer = await PayloadAs(a, a.Observer, id);
        Assert.True(observer.GetProperty("contactsHidden").GetBoolean());
        var hidden = observer.GetProperty("payload");
        Assert.Equal("Aysel", hidden.GetProperty("firstName").GetString());
        Assert.Equal("Rəşid", hidden.GetProperty("fatherName").GetString());
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("phoneNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("email").ValueKind);
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("birthDate").ValueKind);
        // Not anywhere else in the answer either.
        var raw = observer.GetRawText();
        Assert.DoesNotContain("551234567", raw);
        Assert.DoesNotContain("aysel@example.az", raw);
        Assert.DoesNotContain("1994-05-17", raw);

        // The person who asked, the operator who decides, today's manager of the branch and the Company
        // administrator all hold the right (or wrote it), and read everything.
        foreach (var reader in new[] { a.Timekeeper, a.Operator1, a.LegacyManager, a.W.Admin1 })
        {
            var full = await PayloadAs(a, reader, id);
            Assert.False(full.GetProperty("contactsHidden").GetBoolean());
            Assert.Equal("551234567", full.GetProperty("payload").GetProperty("phoneNumber").GetString());
            Assert.Equal("aysel@example.az", full.GetProperty("payload").GetProperty("email").GetString());
            Assert.Equal("1994-05-17", full.GetProperty("payload").GetProperty("birthDate").GetString());
        }
    }

    [Fact]
    public async Task A_stand_in_reads_what_they_approve_while_it_waits_and_not_after()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, PersonWithDetails(a.W.B1), null, default));
        Assert.Equal(201, (await a.Areas().CreateSubstitutionAsync(a.W.Admin1,
            new SubstitutionBody(a.S1, a.Operator2, null, a.Clock.Now.UtcDateTime.AddDays(2), null), null, default)).Status);

        // M5 holds no contact right at B1. Deciding, they see what they decide.
        Assert.False((await PayloadAs(a, a.Operator2, id)).GetProperty("contactsHidden").GetBoolean());
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator2, id, null, default)).Status);
        // Decided: the ordinary line again.
        Assert.True((await PayloadAs(a, a.Operator2, id)).GetProperty("contactsHidden").GetBoolean());
    }

    [Fact]
    public async Task Once_the_account_is_above_plain_staff_only_the_company_administrator_reads_its_numbers()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, PersonWithDetails(a.W.B1), null, default));
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
        using (var db = a.Db())
        {
            db.Employees.Single(e => e.PhoneNumber == "551234567").Role = EmployeeRole.Manager;
            await db.SaveChangesAsync();
        }

        Assert.True((await PayloadAs(a, a.Timekeeper, id)).GetProperty("contactsHidden").GetBoolean());
        Assert.True((await PayloadAs(a, a.Operator1, id)).GetProperty("contactsHidden").GetBoolean());
        Assert.False((await PayloadAs(a, a.W.Admin1, id)).GetProperty("contactsHidden").GetBoolean());
    }

    [Fact]
    public void Todays_manager_and_the_new_roles_except_the_operator_never_decide()
    {
        // The owner's approval of 2026-11-11: the request rights on today's manager are a transition, and
        // never the right to decide.
        Assert.DoesNotContain(AccessPermissions.RequestsDecide, AccessRoleTemplates.LegacyManagerTemplate.Permissions);
        Assert.Contains(AccessPermissions.RequestsCreate, AccessRoleTemplates.LegacyManagerTemplate.Permissions);
        Assert.Contains(AccessPermissions.RequestsView, AccessRoleTemplates.LegacyManagerTemplate.Permissions);
        Assert.DoesNotContain(AccessPermissions.RequestsDecide, AccessRoleTemplates.PinHelperTemplate.Permissions);
        Assert.DoesNotContain(AccessPermissions.RequestsDecide, AccessRoleTemplates.TimekeeperTemplate.Permissions);
        Assert.DoesNotContain(AccessPermissions.RequestsDecide, AccessRoleTemplates.ObserverTemplate.Permissions);
        Assert.Contains(AccessPermissions.RequestsDecide, AccessRoleTemplates.StructureOperatorTemplate.Permissions);
    }

    // --- once, and only once ------------------------------------------------------------------------------

    [Fact]
    public async Task Two_approvers_pressing_at_once_get_one_decision_and_one_account()
    {
        using var a = await CreateAsync();
        await a.GiveAsync(a.W.M2, AccessRoleTemplates.StructureOperator, structure: a.S1);
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        // The first approver has read the request and is about to write when the second one's approval lands.
        DeskResult? second = null;
        var first = await a.Desk(interceptor: new BeforeFirstSave(async () =>
            second = await a.Desk().ApproveAsync(a.W.M2, id, null, default))).ApproveAsync(a.Operator1, id, null, default);

        Assert.Equal(200, second!.Status);
        Assert.Equal(409, first.Status);
        Assert.Equal("AlreadyDecided", Json(first.Body).GetProperty("error").GetString());
        Assert.Single(a.PeopleWithPhone("551234567"));
        Assert.Equal(a.W.M2, a.Request(id).DecidedByEmployeeId);
        Assert.Single(a.History(id), k => k == ChangeRequestEventKind.Approved);
    }

    [Fact]
    public async Task A_decision_is_final()
    {
        using var a = await CreateAsync();
        await a.GiveAsync(a.W.M2, AccessRoleTemplates.StructureOperator, structure: a.S1);
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);

        // The same approver pressing again (a retry) gets the same answer, without a second account or PIN.
        var again = await a.Desk().ApproveAsync(a.Operator1, id, null, default);
        Assert.Equal(200, again.Status);
        Assert.True(Json(again.Body).GetProperty("request").GetProperty("replayed").GetBoolean());
        Assert.False(Json(again.Body).TryGetProperty("tempPin", out _));
        // Anybody else, or any other decision, is told it is decided.
        Assert.Equal(409, (await a.Desk().ApproveAsync(a.W.M2, id, null, default)).Status);
        Assert.Equal(409, (await a.Desk().RejectAsync(a.Operator1, id, "Fikrimi dəyişdim", default)).Status);
        Assert.Equal(409, (await a.Desk().CancelAsync(a.Timekeeper, id, null, default)).Status);
        Assert.Single(a.PeopleWithPhone("551234567"));
    }

    [Fact]
    public async Task The_same_press_twice_is_one_request_and_the_same_person_cannot_wait_twice()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);

        var first = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), "same-press", default);
        var retry = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), "same-press", default);
        Assert.Equal(201, first.Status);
        Assert.Equal(200, retry.Status);
        Assert.Equal(IdOf(first), IdOf(retry));
        Assert.True(Json(retry.Body).GetProperty("replayed").GetBoolean());
        Assert.Single(a.Db().ChangeRequests.AsNoTracking());

        // Somebody else's key gives them nothing.
        var stolen = await a.Desk().CreateEmployeeAsync(a.LegacyManager, Person(a.W.B1, "557777777"), "same-press", default);
        Assert.Equal(409, stolen.Status);
        Assert.Equal("IdempotencyKeyReused", Json(stolen.Body).GetProperty("error").GetString());

        // A new press for the same phone while the first waits: refused, naming the request that waits.
        var duplicate = await a.Desk().CreateEmployeeAsync(a.LegacyManager, Person(a.W.B1, first: "Başqa"), null, default);
        Assert.Equal(409, duplicate.Status);
        Assert.Equal("DuplicatePendingRequest", Json(duplicate.Body).GetProperty("error").GetString());
        Assert.Equal(a.Request(IdOf(first)).Number, Json(duplicate.Body).GetProperty("number").GetInt32());

        // Once the first is rejected, the person can be asked for again.
        Assert.Equal(200, (await a.Desk().RejectAsync(a.Operator1, IdOf(first), "Telefon nömrəsi səhvdir", default)).Status);
        Assert.Equal(201, (await a.Desk().CreateEmployeeAsync(a.LegacyManager, Person(a.W.B1), null, default)).Status);
    }

    [Fact]
    public async Task Requests_are_numbered_in_order_inside_the_company()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var one = a.Request(IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000001"), null, default)));
        var two = a.Request(IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, "551000002"), null, default)));
        Assert.Equal(1001, one.Number);
        Assert.Equal(1002, two.Number);
    }

    // --- reject and cancel --------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ok")]
    public async Task A_rejection_needs_a_reason(string? reason)
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        var result = await a.Desk().RejectAsync(a.Operator1, id, reason, default);
        Assert.Equal(400, result.Status);
        Assert.Equal("RejectReasonRequired", Json(result.Body).GetProperty("error").GetString());
        Assert.Equal(ChangeRequestStatus.Pending, a.Request(id).Status);
    }

    [Fact]
    public async Task A_rejection_keeps_its_reason_and_creates_nothing()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        Assert.Equal(200, (await a.Desk().RejectAsync(a.Operator1, id, "  Şəxsiyyət vəsiqəsinin surəti yoxdur  ", default)).Status);
        var request = a.Request(id);
        Assert.Equal(ChangeRequestStatus.Rejected, request.Status);
        Assert.Equal("Şəxsiyyət vəsiqəsinin surəti yoxdur", request.DecisionNote);
        Assert.Null(request.AppliedAtUtc);
        Assert.Empty(a.PeopleWithPhone("551234567"));
        Assert.Equal([ChangeRequestEventKind.Created, ChangeRequestEventKind.Rejected], a.History(id));
        var detail = Json((await a.Desk().DetailAsync(a.Timekeeper, id, default)).Body);
        Assert.Equal("Şəxsiyyət vəsiqəsinin surəti yoxdur", detail.GetProperty("decisionNote").GetString());
    }

    [Fact]
    public async Task Only_the_person_who_asked_withdraws_it()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        var byOperator = await a.Desk().CancelAsync(a.Operator1, id, null, default);
        Assert.Equal(403, byOperator.Status);
        Assert.Equal("OnlyRequesterCanCancel", Json(byOperator.Body).GetProperty("error").GetString());

        Assert.Equal(200, (await a.Desk().CancelAsync(a.Timekeeper, id, "Səhv filial", default)).Status);
        Assert.Equal(ChangeRequestStatus.Cancelled, a.Request(id).Status);
        Assert.Equal(409, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
        Assert.Empty(a.PeopleWithPhone("551234567"));
    }

    // --- an approval that cannot be applied --------------------------------------------------------------

    [Fact]
    public async Task A_number_taken_since_the_request_refuses_the_approval_and_leaves_the_request_waiting()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));
        // Meanwhile the number is given to somebody else (outside the area, directly).
        Assert.Equal(200, Read(await a.ManagerDesk(a.W.M4).CreateEmployee(DeskPerson(a.W.B4, "551234567", "Başqa", "Adam"))).Status);

        var result = await a.Desk().ApproveAsync(a.Operator1, id, null, default);
        Assert.Equal(409, result.Status);
        Assert.Equal("ApplyRefused", Json(result.Body).GetProperty("error").GetString());
        Assert.Equal("PhoneAlreadyExists", Json(result.Body).GetProperty("reason").GetString());

        var request = a.Request(id);
        Assert.Equal(ChangeRequestStatus.Pending, request.Status);
        Assert.Null(request.DecidedByEmployeeId);
        Assert.Equal("Başqa Adam", Assert.Single(a.PeopleWithPhone("551234567")).FullName);
        Assert.Contains(ChangeRequestEventKind.ApplyRefused, a.History(id));
        // The operator can still say no, with the reason.
        Assert.Equal(200, (await a.Desk().RejectAsync(a.Operator1, id, "Nömrə artıq başqasındadır", default)).Status);
    }

    [Fact]
    public async Task When_writing_the_account_fails_nothing_of_the_approval_stays()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));

        var result = await a.Desk(interceptor: new RefuseNewAccounts()).ApproveAsync(a.Operator1, id, null, default);
        Assert.Equal(409, result.Status);
        Assert.Equal("ApplyFailed", Json(result.Body).GetProperty("reason").GetString());

        Assert.Empty(a.PeopleWithPhone("551234567"));
        var request = a.Request(id);
        Assert.Equal(ChangeRequestStatus.Pending, request.Status);
        Assert.Null(request.DecidedByEmployeeId);
        Assert.Null(request.AppliedAtUtc);
        Assert.Null(request.EmployeeId);

        // And the next press works.
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
        Assert.Single(a.PeopleWithPhone("551234567"));
    }

    // --- the desk's own rules, unchanged -----------------------------------------------------------------

    [Fact]
    public async Task A_request_is_held_to_the_rules_of_adding_somebody_directly()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var desk = a.Desk();

        async Task<string?> Refusal(EmployeeCreateRequestBody body) =>
            Json((await a.Desk().CreateEmployeeAsync(a.Timekeeper, body, null, default)).Body).GetProperty("error").GetString();

        Assert.Equal("NameRequired", await Refusal(Person(a.W.B1, first: " ")));
        Assert.Equal("NeedEmailOrPhone", await Refusal(Person(a.W.B1, phone: "")));
        // Somebody in the company already logs in with this number.
        var taken = a.Db().Employees.AsNoTracking().Single(e => e.Id == a.W.E2).PhoneNumber!;
        Assert.Equal("PhoneAlreadyExists", await Refusal(Person(a.W.B1, phone: taken)));
        // Job titles come from the catalogue.
        Assert.Equal("PositionNotInCatalogue", await Refusal(Person(a.W.B1, position: "Uydurma vəzifə")));
        // A shift pinned to another branch is not this branch's to use.
        var shift = new Schedule { TenantId = AccessWorld.TenantA, Name = "B3 səhər", LocationId = a.W.B3,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0) };
        using (var db = a.Db()) { db.Schedules.Add(shift); await db.SaveChangesAsync(); }
        Assert.Equal("ScheduleBelongsToOtherBranch", await Refusal(Person(a.W.B1, schedule: shift.Id)));
        Assert.Equal("NoteTooLong", await Refusal(Person(a.W.B1, note: new string('a', 501))));

        Assert.Empty(a.Db().ChangeRequests.AsNoTracking());
        Assert.Equal(201, (await desk.CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default)).Status);
    }

    [Fact]
    public void Only_pending_moves_and_only_once()
    {
        foreach (var to in Enum.GetValues<ChangeRequestStatus>())
        {
            Assert.Equal(to != ChangeRequestStatus.Pending, ChangeRequestRules.CanMove(ChangeRequestStatus.Pending, to));
            foreach (var from in new[] { ChangeRequestStatus.Approved, ChangeRequestStatus.Rejected, ChangeRequestStatus.Cancelled })
                Assert.False(ChangeRequestRules.CanMove(from, to));
        }
    }

    // --- helpers ------------------------------------------------------------------------------------------

    /// <summary>Runs <paramref name="other"/> just before this context's first save: the other approver's whole
    /// approval lands between this one's read and its write.</summary>
    private sealed class BeforeFirstSave(Func<Task> other) : SaveChangesInterceptor
    {
        private bool _done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!_done)
            {
                _done = true;
                await other();
            }
            return result;
        }
    }

    /// <summary>The database refusing the new account, as it would a number taken in the same second.</summary>
    private sealed class RefuseNewAccounts : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Employee>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated: the database refused the new account");
            return ValueTask.FromResult(result);
        }
    }
}
