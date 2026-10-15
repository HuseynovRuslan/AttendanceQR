using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static AttendanceQR.Domain.Access.AccessPermissions;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The new way of working (docs/strukturlar-ve-selahiyyetler.md §3.4–3.5): a structure operator over
/// «Aeroport yolu», a timekeeper over two branches, an observer who only reads — and the rules that stop
/// anybody from raising their own power or handing out power they do not hold.
/// </summary>
public class AccessRolesAndRulesTests
{
    private sealed class Org : IDisposable
    {
        public AccessWorld W { get; } = new();
        public Guid Airport { get; } = Guid.NewGuid();   // structure: B1, B2
        public Guid Parks { get; } = Guid.NewGuid();     // structure: B3
        public Dictionary<string, AccessRole> Roles { get; private set; } = [];

        public async Task InitAsync()
        {
            var db = W.Context(AccessWorld.TenantA);
            db.Structures.Add(new Structure { Id = Airport, TenantId = AccessWorld.TenantA, Name = "Aeroport yolu" });
            db.Structures.Add(new Structure { Id = Parks, TenantId = AccessWorld.TenantA, Name = "Parklar" });
            await db.SaveChangesAsync();
            foreach (var (branch, structure) in new[] { (W.B1, Airport), (W.B2, Airport), (W.B3, Parks) })
                db.Locations.Single(l => l.Id == branch).StructureId = structure;
            await db.SaveChangesAsync();
            await AccessSeeder.ReconcileTenantAsync(db, AccessWorld.TenantA);
            Roles = await db.AccessRoles.AsNoTracking().Where(r => r.TemplateKey != null).ToDictionaryAsync(r => r.TemplateKey!);
        }

        public async Task GiveAsync(Guid employee, string template, AccessScopeType scope, Guid? structure = null, Guid? location = null)
        {
            var db = W.Context(AccessWorld.TenantA);
            db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = Roles[template].Id, Scope = scope,
                StructureId = structure, LocationId = location, CreatedByEmployeeId = W.Admin1,
            });
            await db.SaveChangesAsync();
        }

        public Task<AccessGraph> GraphAsync() => AccessGraph.LoadAsync(W.Context(AccessWorld.TenantA));

        public void Dispose() => W.Dispose();
    }

    [Fact]
    public async Task A_structure_operator_reaches_every_branch_of_their_structure_and_no_other()
    {
        using var o = new Org();
        await o.InitAsync();
        await o.GiveAsync(o.W.E4, AccessRoleTemplates.StructureOperator, AccessScopeType.Structure, structure: o.Airport);

        var op = (await o.GraphAsync()).For(o.W.E4);

        Assert.True(op.Covers(EmployeesUpdate, o.W.B1));
        Assert.True(op.Covers(EmployeesUpdate, o.W.B2));
        Assert.False(op.Covers(EmployeesUpdate, o.W.B3));
        Assert.False(op.HasAtCompany(EmployeesView));
    }

    [Fact]
    public async Task A_branch_moved_into_the_structure_is_reached_at_once()
    {
        using var o = new Org();
        await o.InitAsync();
        await o.GiveAsync(o.W.E4, AccessRoleTemplates.StructureOperator, AccessScopeType.Structure, structure: o.Airport);
        using (var db = o.W.Context(AccessWorld.TenantA))
        {
            db.Locations.Single(l => l.Id == o.W.B5).StructureId = o.Airport;
            db.SaveChanges();
        }

        Assert.True((await o.GraphAsync()).For(o.W.E4).Covers(AttendanceView, o.W.B5));
    }

    [Fact]
    public async Task A_timekeeper_on_two_branches_works_on_both_and_nowhere_else()
    {
        using var o = new Org();
        await o.InitAsync();
        await o.GiveAsync(o.W.E1, AccessRoleTemplates.Timekeeper, AccessScopeType.Branch, location: o.W.B1);
        await o.GiveAsync(o.W.E1, AccessRoleTemplates.Timekeeper, AccessScopeType.Branch, location: o.W.B4);

        var tk = (await o.GraphAsync()).For(o.W.E1);

        Assert.True(tk.Covers(EmployeesResetPin, o.W.B1));
        Assert.True(tk.Covers(EmployeesResetPin, o.W.B4));
        Assert.False(tk.Covers(EmployeesResetPin, o.W.B2));
        Assert.False(tk.Has(AttendanceCorrect));     // needs its own permission
        Assert.False(tk.Has(FieldCheckInGrant));
    }

    [Fact]
    public async Task An_observer_reads_and_writes_nothing()
    {
        using var o = new Org();
        await o.InitAsync();
        await o.GiveAsync(o.W.E5, AccessRoleTemplates.Observer, AccessScopeType.Structure, structure: o.Parks);

        var observer = (await o.GraphAsync()).For(o.W.E5);

        Assert.True(observer.Covers(AttendanceView, o.W.B3));
        Assert.All(observer.Permissions, p => Assert.DoesNotContain(p, new[]
        {
            AttendanceCorrect, EmployeesCreate, EmployeesUpdate, EmployeesResetPin, DevicesApprove, SchedulesManage, LeavesManage,
        }));
    }

    [Fact]
    public async Task Revoked_assignments_and_archived_roles_grant_nothing()
    {
        using var o = new Org();
        await o.InitAsync();
        await o.GiveAsync(o.W.E1, AccessRoleTemplates.Timekeeper, AccessScopeType.Branch, location: o.W.B1);
        await o.GiveAsync(o.W.E2, AccessRoleTemplates.Observer, AccessScopeType.Branch, location: o.W.B2);
        using (var db = o.W.Context(AccessWorld.TenantA))
        {
            db.RoleAssignments.Single(a => a.EmployeeId == o.W.E1).RevokedAtUtc = DateTime.UtcNow;
            db.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.Observer).ArchivedAtUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        var graph = await o.GraphAsync();

        Assert.Empty(graph.For(o.W.E1).Permissions);
        Assert.Empty(graph.For(o.W.E2).Permissions);
    }

    [Fact]
    public async Task An_operator_manages_the_timekeepers_under_them_and_a_timekeeper_never_manages_the_operator()
    {
        using var o = new Org();
        await o.InitAsync();
        await o.GiveAsync(o.W.E4, AccessRoleTemplates.StructureOperator, AccessScopeType.Structure, structure: o.Airport);
        await o.GiveAsync(o.W.E1, AccessRoleTemplates.Timekeeper, AccessScopeType.Branch, location: o.W.B1);
        var graph = await o.GraphAsync();
        var op = graph.For(o.W.E4);
        var tk = graph.For(o.W.E1);

        Assert.True(AccessRules.CanActOn(op, EmployeesResetPin, o.W.E1, o.W.B1, tk, false, refuseOperators: true, refuseSelf: true));
        // The operator's home is B4, outside the timekeeper's reach — and even at B1 the operator would hold
        // powers over B2 the timekeeper does not.
        Assert.False(AccessRules.Dominates(tk, op, o.W.B1));
    }

    [Fact]
    public async Task Nobody_raises_their_own_power()
    {
        using var o = new Org();
        await o.InitAsync();
        var graph = await o.GraphAsync();
        var admin = graph.For(o.W.Admin1);

        var decision = AccessRules.CanGrant(admin, AccessRoleTemplates.ObserverTemplate.Permissions, AccessScopeType.Branch,
            null, o.W.B1, assigneeId: o.W.Admin1, assigneeIsOperator: false);

        Assert.Equal("SelfGrant", decision.Error);
    }

    [Fact]
    public async Task Nobody_hands_out_a_power_they_do_not_hold_or_beyond_their_area()
    {
        using var o = new Org();
        await o.InitAsync();
        // An operator over Aeroport yolu who has also been allowed to assign people there.
        var db = o.W.Context(AccessWorld.TenantA);
        var assigner = new AccessRole
        {
            TenantId = AccessWorld.TenantA, Name = "Operator + təyinat",
            Permissions = [.. AccessRoleTemplates.StructureOperatorTemplate.Permissions, AssignmentsManage],
        };
        db.AccessRoles.Add(assigner);
        db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = o.W.E4, AccessRoleId = assigner.Id, Scope = AccessScopeType.Structure,
            StructureId = o.Airport, CreatedByEmployeeId = o.W.Admin1,
        });
        await db.SaveChangesAsync();
        var op = (await o.GraphAsync()).For(o.W.E4);

        // A timekeeper at a branch of their structure: yes.
        Assert.True(AccessRules.CanGrant(op, AccessRoleTemplates.TimekeeperTemplate.Permissions, AccessScopeType.Branch,
            null, o.W.B2, o.W.E2, false).Allowed);
        // At a branch of another structure: out of their area.
        Assert.Equal("OutOfScope", AccessRules.CanGrant(op, AccessRoleTemplates.TimekeeperTemplate.Permissions,
            AccessScopeType.Branch, null, o.W.B3, o.W.E3, false).Error);
        // A role carrying a power they do not hold (attendance correction): escalation, refused.
        var withCorrection = AccessRoleTemplates.TimekeeperTemplate.Permissions.Append(AttendanceCorrect).ToList();
        var refused = AccessRules.CanGrant(op, withCorrection, AccessScopeType.Branch, null, o.W.B2, o.W.E2, false);
        Assert.Equal(("PermissionNotHeld", AttendanceCorrect), (refused.Error, refused.Permission));
        // Company-wide: they hold nothing company-wide.
        Assert.Equal("OutOfScope", AccessRules.CanGrant(op, AccessRoleTemplates.ObserverTemplate.Permissions,
            AccessScopeType.Company, null, null, o.W.E2, false).Error);
        // A platform operator's row: never.
        Assert.Equal("CannotManageOperator", AccessRules.CanGrant(op, AccessRoleTemplates.ObserverTemplate.Permissions,
            AccessScopeType.Branch, null, o.W.B1, o.W.Operator, assigneeIsOperator: true).Error);
    }

    [Fact]
    public async Task A_company_only_power_is_never_handed_out_below_the_company()
    {
        using var o = new Org();
        await o.InitAsync();
        var admin = (await o.GraphAsync()).For(o.W.Admin1);

        var refused = AccessRules.CanGrant(admin, [HolidaysManage], AccessScopeType.Structure, o.Airport, null, o.W.E1, false);

        Assert.Equal(("CompanyOnlyPermission", HolidaysManage), (refused.Error, refused.Permission));
        Assert.True(AccessRules.CanGrant(admin, [HolidaysManage], AccessScopeType.Company, null, null, o.W.E1, false).Allowed);
    }
}
