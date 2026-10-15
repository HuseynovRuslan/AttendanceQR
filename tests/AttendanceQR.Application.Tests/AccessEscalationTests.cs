using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using static AttendanceQR.Domain.Access.AccessPermissions;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Phase 3's finish line (docs/strukturlar-ve-selahiyyetler.md §10): nobody raises their own power, hands out
/// power they do not hold, reaches people outside their area, appoints their equal, or changes the account of
/// somebody at their own level — the owner's three rules of 2026-10-15 — and today's access is not touched
/// by any of it. The same rules then hold through the API that hands roles out.
/// </summary>
public class AccessEscalationTests
{
    /// <summary>
    /// «Aeroport yolu» (B1, B2) and «Parklar» (B3). In Aeroport yolu: two operators, one operator who may also
    /// assign, a second such assigner, a timekeeper at B2. Plus today's people from AccessWorld.
    /// </summary>
    private sealed class Airport : IDisposable
    {
        public AccessWorld W { get; } = new();
        public Guid Structure { get; } = Guid.NewGuid();
        public Guid Parks { get; } = Guid.NewGuid();
        public Guid Op1 { get; private set; }
        public Guid Op2 { get; private set; }
        public Guid Assigner { get; private set; }
        public Guid Assigner2 { get; private set; }
        public Guid Keeper { get; private set; }
        public Guid Worker1 { get; private set; }
        public Guid ParksWorker { get; private set; }
        public Dictionary<string, AccessRole> Roles { get; private set; } = [];
        public AccessRole OperatorPlusAssign { get; private set; } = null!;

        public async Task InitAsync()
        {
            var db = W.Context(AccessWorld.TenantA);
            db.Structures.Add(new Structure { Id = Structure, TenantId = AccessWorld.TenantA, Name = "Aeroport yolu" });
            db.Structures.Add(new Structure { Id = Parks, TenantId = AccessWorld.TenantA, Name = "Parklar" });
            await db.SaveChangesAsync();
            foreach (var (branch, structure) in new[] { (W.B1, Structure), (W.B2, Structure), (W.B3, Parks) })
                db.Locations.Single(l => l.Id == branch).StructureId = structure;
            Op1 = Person(db, W.B1);
            Op2 = Person(db, W.B2);
            Assigner = Person(db, W.B1);
            Assigner2 = Person(db, W.B2);
            Keeper = Person(db, W.B2);
            Worker1 = Person(db, W.B1);
            ParksWorker = Person(db, W.B3);
            await db.SaveChangesAsync();
            await AccessSeeder.ReconcileTenantAsync(db, AccessWorld.TenantA);
            Roles = await db.AccessRoles.AsNoTracking().Where(r => r.TemplateKey != null).ToDictionaryAsync(r => r.TemplateKey!);

            OperatorPlusAssign = new AccessRole
            {
                TenantId = AccessWorld.TenantA, Name = "Operator + təyinat",
                Permissions = [.. AccessRoleTemplates.StructureOperatorTemplate.Permissions, AssignmentsManage],
            };
            db.AccessRoles.Add(OperatorPlusAssign);
            await db.SaveChangesAsync();
            await GiveAsync(Op1, Roles[AccessRoleTemplates.StructureOperator], AccessScopeType.Structure, Structure);
            await GiveAsync(Op2, Roles[AccessRoleTemplates.StructureOperator], AccessScopeType.Structure, Structure);
            await GiveAsync(Assigner, OperatorPlusAssign, AccessScopeType.Structure, Structure);
            await GiveAsync(Assigner2, OperatorPlusAssign, AccessScopeType.Structure, Structure);
            await GiveAsync(Keeper, Roles[AccessRoleTemplates.Timekeeper], AccessScopeType.Branch, location: W.B2);
        }

        private static Guid Person(AppDbContext db, Guid home)
        {
            var id = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = id, TenantId = AccessWorld.TenantA, FullName = "İşçi " + id.ToString()[..4], Email = $"{id:N}@a.local",
                Role = EmployeeRole.Employee, LocationId = home, IsActive = true, PasswordHash = "hashed:1234",
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            return id;
        }

        public async Task GiveAsync(Guid employee, AccessRole role, AccessScopeType scope, Guid? structure = null, Guid? location = null)
        {
            var db = W.Context(AccessWorld.TenantA);
            db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = role.Id, Scope = scope,
                StructureId = structure, LocationId = location, CreatedByEmployeeId = W.Admin1,
            });
            await db.SaveChangesAsync();
        }

        public Task<AccessGraph> GraphAsync() => AccessGraph.LoadAsync(W.Context(AccessWorld.TenantA));

        /// <summary>Today's rule table, asked the way the shadow asks it.</summary>
        public bool Decide(AccessGraph g, string rule, Guid actor, Guid target) =>
            ShadowRules.Decide(rule, g.For(actor), target, W.HomeOf(target), g.For(target), target == W.Operator, W.HomeOf(actor));

        public GrantDecision Grant(AccessGraph g, Guid actor, IReadOnlyCollection<string> permissions, AccessScopeType scope,
            Guid? structure, Guid? location, Guid assignee) =>
            AccessRules.CanGrant(g.For(actor), permissions, scope, structure, location, assignee, assignee == W.Operator,
                W.HomeOf(assignee), g.For(assignee), W.HomeOf(actor));

        public AdminAccessController Api(Guid caller, bool impersonating = false)
        {
            var claims = new List<Claim> { new("sub", caller.ToString()), new("role", "Admin") };
            if (impersonating)
                claims.Add(new Claim("imp", Guid.NewGuid().ToString()));
            return new AdminAccessController(W.Context(AccessWorld.TenantA), new OperatorAllowlist(new HashSet<Guid> { W.Operator }))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
                },
            };
        }

        public void Dispose() => W.Dispose();
    }

    private static T Prop<T>(object o, string name) => (T)o.GetType().GetProperty(name)!.GetValue(o)!;

    private static string ErrorOf(IActionResult result) => Prop<string>(((ObjectResult)result).Value!, "error");

    // ───────────────────── Rule 1: accounts are changed only from above ─────────────────────

    [Fact]
    public async Task An_operator_never_changes_the_account_of_a_fellow_operator_of_the_same_structure()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();

        Assert.False(o.Decide(g, ShadowRules.CredentialTarget, o.Op1, o.Op2));
        Assert.False(o.Decide(g, ShadowRules.ManageableEmployee, o.Op1, o.Op2));
        Assert.False(o.Decide(g, ShadowRules.CredentialTarget, o.Op2, o.Op1));
        // …and two assigners of the same structure are each other's equals too.
        Assert.False(o.Decide(g, ShadowRules.CredentialTarget, o.Assigner, o.Assigner2));
    }

    [Fact]
    public async Task An_operator_still_serves_the_people_below_them()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();

        Assert.True(o.Decide(g, ShadowRules.CredentialTarget, o.Op1, o.Worker1));   // plain staff
        Assert.True(o.Decide(g, ShadowRules.ManageableEmployee, o.Op1, o.Keeper));   // a timekeeper under them
        Assert.False(o.Decide(g, ShadowRules.CredentialTarget, o.Keeper, o.Op1));    // never upwards
        // Holding the right to assign puts an operator above one who does not hold it.
        Assert.True(o.Decide(g, ShadowRules.CredentialTarget, o.Assigner, o.Op2));
        Assert.False(o.Decide(g, ShadowRules.CredentialTarget, o.Op2, o.Assigner));
    }

    [Fact]
    public async Task Todays_access_is_untouched_by_the_strict_rule()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();

        // Today's colleague rule: M1 manages B2, where M2 works.
        Assert.True(o.Decide(g, ShadowRules.CredentialTarget, o.W.M1, o.W.M2));
        // Today's admins act on one another — company-wide power is the «company account».
        Assert.True(o.Decide(g, ShadowRules.CredentialTarget, o.W.Admin1, o.W.Admin2));
        Assert.True(o.Decide(g, ShadowRules.ManageableEmployee, o.W.Admin1, o.Op1));
    }

    // ───────────────────── Attendance: never one's own ─────────────────────

    [Fact]
    public async Task Nobody_corrects_their_own_attendance_in_either_model()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();
        var db = o.W.Context(AccessWorld.TenantA);

        foreach (var (who, role) in new[] { (o.W.Admin1, EmployeeRole.Admin), (o.W.M1, EmployeeRole.Manager) })
        {
            Assert.False(await LocationScopeRules.CanCorrectAttendanceAsync(db, who, role, who, default));
            Assert.False(o.Decide(g, ShadowRules.CorrectAttendance, who, who));
        }
        // Somebody else's: today's answer, unchanged.
        Assert.True(await LocationScopeRules.CanCorrectAttendanceAsync(db, o.W.Admin1, EmployeeRole.Admin, o.W.E1, default));
        Assert.True(o.Decide(g, ShadowRules.CorrectAttendance, o.W.Admin1, o.W.E1));
        Assert.True(await LocationScopeRules.CanCorrectAttendanceAsync(db, o.W.M1, EmployeeRole.Manager, o.W.E1, default));
        Assert.True(o.Decide(g, ShadowRules.CorrectAttendance, o.W.M1, o.W.E1));
        // The records rule beside it (devices, requests) keeps today's answer for an admin's own records.
        Assert.True(o.Decide(g, ShadowRules.CanManage, o.W.Admin1, o.W.Admin1));
    }

    // ───────────────────── Rules 2 and 3: handing roles out ─────────────────────

    [Fact]
    public async Task Rule_2_an_assigner_reaches_only_their_own_people_unless_they_hold_the_wider_right()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();
        var observer = AccessRoleTemplates.ObserverTemplate.Permissions;

        Assert.True(o.Grant(g, o.Assigner, observer, AccessScopeType.Structure, o.Structure, null, o.Worker1).Allowed);
        Assert.Equal("AssigneeOutOfScope",
            o.Grant(g, o.Assigner, observer, AccessScopeType.Structure, o.Structure, null, o.ParksWorker).Error);

        // The separate, company-only right to reach further.
        var wider = new AccessRole { TenantId = AccessWorld.TenantA, Name = "Kənara təyinat", Permissions = [AssignmentsCrossArea] };
        var db = o.W.Context(AccessWorld.TenantA);
        db.AccessRoles.Add(wider);
        await db.SaveChangesAsync();
        await o.GiveAsync(o.Assigner, wider, AccessScopeType.Company);
        g = await o.GraphAsync();

        Assert.True(o.Grant(g, o.Assigner, observer, AccessScopeType.Structure, o.Structure, null, o.ParksWorker).Allowed);
        // Reaching a person is not reaching their branch: the area is still only Aeroport yolu.
        Assert.Equal("OutOfScope",
            o.Grant(g, o.Assigner, observer, AccessScopeType.Structure, o.Parks, null, o.ParksWorker).Error);
    }

    [Fact]
    public async Task Rule_3_below_company_level_only_a_role_below_your_own()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();

        // Down: a timekeeper or an observer.
        Assert.True(o.Grant(g, o.Assigner, AccessRoleTemplates.TimekeeperTemplate.Permissions, AccessScopeType.Branch, null, o.W.B1, o.Worker1).Allowed);
        Assert.True(o.Grant(g, o.Assigner, AccessRoleTemplates.ObserverTemplate.Permissions, AccessScopeType.Structure, o.Structure, null, o.Worker1).Allowed);
        // Level: an operator — even one without the right to assign, which is left out of the comparison.
        Assert.Equal("NotBelowYou", o.Grant(g, o.Assigner, AccessRoleTemplates.StructureOperatorTemplate.Permissions,
            AccessScopeType.Structure, o.Structure, null, o.Worker1).Error);
        Assert.Equal("NotBelowYou", o.Grant(g, o.Assigner, o.OperatorPlusAssign.Permissions,
            AccessScopeType.Structure, o.Structure, null, o.Worker1).Error);
        // The company account may appoint operators, assigners included.
        Assert.True(o.Grant(g, o.W.Admin1, o.OperatorPlusAssign.Permissions, AccessScopeType.Structure, o.Structure, null, o.Worker1).Allowed);
    }

    [Fact]
    public async Task Rule_1_an_assigner_never_changes_the_roles_of_their_equal()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();
        var observer = AccessRoleTemplates.ObserverTemplate.Permissions;

        Assert.Equal("AssigneeNotBelowYou",
            o.Grant(g, o.Assigner, observer, AccessScopeType.Structure, o.Structure, null, o.Assigner2).Error);
        Assert.True(o.Grant(g, o.Assigner, observer, AccessScopeType.Structure, o.Structure, null, o.Op2).Allowed);
        // Today's managers hold no right to assign at all.
        Assert.Equal("OutOfScope", o.Grant(g, o.W.M1, observer, AccessScopeType.Branch, null, o.W.B1, o.W.E1).Error);
    }

    [Fact]
    public async Task Taking_away_follows_the_same_rules_and_never_touches_todays_rows()
    {
        using var o = new Airport();
        await o.InitAsync();
        var g = await o.GraphAsync();
        AccessRole RoleOf(string key) => o.Roles[key];
        GrantDecision Revoke(Guid actor, bool system, AccessRole role, AccessScopeType scope, Guid? s, Guid? l, Guid assignee) =>
            AccessRules.CanRevoke(g.For(actor), system, role.Permissions, scope, s, l, assignee, assignee == o.W.Operator,
                o.W.HomeOf(assignee), g.For(assignee), o.W.HomeOf(actor));

        Assert.True(Revoke(o.Assigner, false, RoleOf(AccessRoleTemplates.Timekeeper), AccessScopeType.Branch, null, o.W.B2, o.Keeper).Allowed);
        Assert.Equal("NotBelowYou", Revoke(o.Assigner, false, RoleOf(AccessRoleTemplates.StructureOperator),
            AccessScopeType.Structure, o.Structure, null, o.Op2).Error);
        Assert.True(Revoke(o.W.Admin1, false, RoleOf(AccessRoleTemplates.StructureOperator), AccessScopeType.Structure, o.Structure, null, o.Op2).Allowed);
        Assert.Equal("SystemAssignment", Revoke(o.W.Admin1, true, RoleOf(AccessRoleTemplates.LegacyManager),
            AccessScopeType.Branch, null, o.W.B1, o.W.M1).Error);
        Assert.Equal("SelfRevoke", Revoke(o.Assigner, false, o.OperatorPlusAssign, AccessScopeType.Structure, o.Structure, null, o.Assigner).Error);
    }

    // ───────────────────── The API that hands roles out ─────────────────────

    [Fact]
    public async Task A_timekeeper_over_two_branches_counts_from_the_next_request_without_signing_anybody_out()
    {
        using var o = new Airport();
        await o.InitAsync();
        var tokenVersion = o.W.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(e => e.Id == o.W.E1).TokenVersion;

        var ok = Assert.IsType<OkObjectResult>(await o.Api(o.W.Admin1).Assign(new AssignRoleRequest(
            o.W.E1, o.Roles[AccessRoleTemplates.Timekeeper].Id, AccessScopeType.Branch, LocationIds: [o.W.B1, o.W.B2])));

        Assert.Equal(2, Prop<IEnumerable<object>>(ok.Value!, "created").Count());
        var access = (await o.GraphAsync()).For(o.W.E1);
        Assert.True(access.Covers(EmployeesResetPin, o.W.B1) && access.Covers(EmployeesResetPin, o.W.B2));
        Assert.False(access.Covers(EmployeesResetPin, o.W.B3));
        var db = o.W.Context(AccessWorld.TenantA);
        Assert.Equal(2, db.AuditLogs.Count(a => a.EventType == AuditEventType.AccessGranted && a.EmployeeId == o.W.E1));
        Assert.All(db.RoleAssignments.Where(a => a.EmployeeId == o.W.E1), a => Assert.Equal(o.W.Admin1, a.CreatedByEmployeeId));
        // The same panel request a minute later sees it — and the token was never touched.
        var me = new MeController(o.W.Context(AccessWorld.TenantA), new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", o.W.E1.ToString()), new Claim("role", "Employee")], "test")),
                },
            },
        };
        var keys = Prop<System.Collections.IEnumerable>(Assert.IsType<OkObjectResult>(await me.Access()).Value!, "permissions")
            .Cast<object>().Select(p => Prop<string>(p, "key")).ToList();
        Assert.Contains(EmployeesResetPin, keys);
        Assert.Equal(tokenVersion, o.W.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(e => e.Id == o.W.E1).TokenVersion);

        // Again: counted, not doubled.
        var again = Assert.IsType<OkObjectResult>(await o.Api(o.W.Admin1).Assign(new AssignRoleRequest(
            o.W.E1, o.Roles[AccessRoleTemplates.Timekeeper].Id, AccessScopeType.Branch, LocationIds: [o.W.B1, o.W.B2])));
        Assert.Equal(2, Prop<int>(again.Value!, "alreadyHeld"));
    }

    [Fact]
    public async Task Taken_away_it_is_gone_from_the_next_request_and_stays_on_the_record()
    {
        using var o = new Airport();
        await o.InitAsync();
        var row = o.W.Context(AccessWorld.TenantA).RoleAssignments.AsNoTracking().Single(a => a.EmployeeId == o.Keeper);

        Assert.IsType<OkObjectResult>(await o.Api(o.W.Admin1).Revoke(row.Id));

        Assert.Empty((await o.GraphAsync()).For(o.Keeper).Grants);
        var db = o.W.Context(AccessWorld.TenantA);
        var after = db.RoleAssignments.AsNoTracking().Single(a => a.Id == row.Id);
        Assert.Equal(o.W.Admin1, after.RevokedByEmployeeId);
        Assert.Single(db.AuditLogs.Where(a => a.EventType == AuditEventType.AccessRevoked && a.EmployeeId == o.Keeper));
        Assert.Equal("AlreadyRevoked", ErrorOf(await o.Api(o.W.Admin1).Revoke(row.Id)));
    }

    [Fact]
    public async Task The_api_refuses_every_way_round()
    {
        using var o = new Airport();
        await o.InitAsync();
        var api = o.Api(o.W.Admin1);
        var observer = o.Roles[AccessRoleTemplates.Observer].Id;
        var before = o.W.Context(AccessWorld.TenantA).RoleAssignments.Count();

        // Today's two roles follow the employee form, nothing else.
        Assert.Equal("LegacyRole", ErrorOf(await api.Assign(new AssignRoleRequest(
            o.W.E1, o.Roles[AccessRoleTemplates.LegacyManager].Id, AccessScopeType.Branch, LocationIds: [o.W.B1]))));
        // A platform operator's row, oneself, a support session.
        Assert.Equal("CannotManageOperator", ErrorOf(await api.Assign(new AssignRoleRequest(
            o.W.Operator, observer, AccessScopeType.Branch, LocationIds: [o.W.B2]))));
        Assert.Equal("SelfGrant", ErrorOf(await api.Assign(new AssignRoleRequest(
            o.W.Admin1, observer, AccessScopeType.Branch, LocationIds: [o.W.B1]))));
        Assert.Equal("NotDuringImpersonation", ErrorOf(await o.Api(o.W.Admin1, impersonating: true).Assign(new AssignRoleRequest(
            o.W.E1, observer, AccessScopeType.Branch, LocationIds: [o.W.B1]))));
        // A shape that does not fit its scope.
        Assert.Equal("ScopeMismatch", ErrorOf(await api.Assign(new AssignRoleRequest(
            o.W.E1, observer, AccessScopeType.Structure, LocationIds: [o.W.B1]))));
        // Another company's branch, person — invisible here, so «not found», never «forbidden».
        Assert.Equal("LocationNotFound", ErrorOf(await api.Assign(new AssignRoleRequest(
            o.W.E1, observer, AccessScopeType.Branch, LocationIds: [o.W.OtherBranch]))));
        Assert.Equal("EmployeeNotFound", ErrorOf(await api.Assign(new AssignRoleRequest(
            o.W.OtherCompanyManager, observer, AccessScopeType.Branch, LocationIds: [o.W.B1]))));
        // A system row cannot be taken away by hand.
        var system = o.W.Context(AccessWorld.TenantA).RoleAssignments.AsNoTracking().First(a => a.EmployeeId == o.W.M1 && a.CreatedByEmployeeId == null);
        Assert.Equal("SystemAssignment", ErrorOf(await api.Revoke(system.Id)));

        Assert.Equal(before, o.W.Context(AccessWorld.TenantA).RoleAssignments.Count());
    }

    [Fact]
    public async Task Roles_are_edited_carefully()
    {
        using var o = new Airport();
        await o.InitAsync();
        var api = o.Api(o.W.Admin1);

        var created = Assert.IsType<OkObjectResult>(await api.CreateRole(new AccessRoleRequest(
            "Gecə növbəsi nəzarətçisi", null, [AttendanceView, EmployeesView])));
        var id = Prop<Guid>(created.Value!, "id");
        Assert.Equal("NameTaken", ErrorOf(await api.CreateRole(new AccessRoleRequest("Gecə növbəsi nəzarətçisi", null, [AttendanceView]))));
        Assert.Equal("UnknownPermission", ErrorOf(await api.CreateRole(new AccessRoleRequest("Yeni rol", null, ["payroll.export.all"]))));
        Assert.Equal("RoleLocked", ErrorOf(await api.UpdateRole(o.Roles[AccessRoleTemplates.CompanyAdmin].Id,
            new AccessRoleRequest("Admin", null, [AttendanceView]))));
        Assert.Equal("NotDuringImpersonation", ErrorOf(await o.Api(o.W.Admin1, impersonating: true)
            .CreateRole(new AccessRoleRequest("Dəstək rolu", null, [AttendanceView]))));

        // Held at a branch, the role cannot be given a company-only right.
        Assert.IsType<OkObjectResult>(await api.Assign(new AssignRoleRequest(o.W.E1, id, AccessScopeType.Branch, LocationIds: [o.W.B1])));
        Assert.Equal("CompanyOnlyPermissionInAreaAssignment", ErrorOf(await api.UpdateRole(id,
            new AccessRoleRequest("Gecə növbəsi nəzarətçisi", null, [AttendanceView, EmployeesView, HolidaysManage]))));
        // In use: not archived; freed: archived, and restorable.
        Assert.Equal("RoleInUse", ErrorOf(await api.ArchiveRole(id)));
        var row = o.W.Context(AccessWorld.TenantA).RoleAssignments.AsNoTracking().Single(a => a.AccessRoleId == id);
        Assert.IsType<OkObjectResult>(await api.Revoke(row.Id));
        Assert.True(Prop<bool>(Assert.IsType<OkObjectResult>(await api.ArchiveRole(id)).Value!, "archived"));
        Assert.False(Prop<bool>(Assert.IsType<OkObjectResult>(await api.RestoreRole(id)).Value!, "archived"));
        // Created, archived, restored — the refused edit wrote nothing.
        Assert.Equal(3, o.W.Context(AccessWorld.TenantA).AuditLogs.Count(a => a.EventType == AuditEventType.AccessRoleChanged));
    }

    [Fact]
    public async Task The_dialog_offers_only_what_would_be_allowed()
    {
        using var o = new Airport();
        await o.InitAsync();
        var api = o.Api(o.W.Admin1);

        var offer = Assert.IsType<OkObjectResult>(await api.Assignable(o.Worker1)).Value!;
        var keys = Prop<System.Collections.IEnumerable>(offer, "roles").Cast<object>()
            .Select(r => Prop<string?>(r, "templateKey")).ToList();
        Assert.Contains(AccessRoleTemplates.StructureOperator, keys);
        Assert.Contains(AccessRoleTemplates.Timekeeper, keys);
        Assert.DoesNotContain(AccessRoleTemplates.LegacyManager, keys);
        Assert.DoesNotContain(AccessRoleTemplates.PinHelper, keys);

        Assert.Equal("SelfGrant", Prop<string>(Assert.IsType<OkObjectResult>(await api.Assignable(o.W.Admin1)).Value!, "reason"));
        Assert.Equal("CannotManageOperator", Prop<string>(Assert.IsType<OkObjectResult>(await api.Assignable(o.W.Operator)).Value!, "reason"));
    }
}
