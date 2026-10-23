using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Today's Admin and Manager access, mirrored into the permission model (AccessSeeder): nobody gains, nobody
/// loses, nothing a person assigned is touched, and drift — a role or a branch changed without the model
/// being told — heals on the next reconcile.
/// </summary>
public class AccessSeederTests
{
    private static async Task<List<(string Template, AccessScopeType Scope, Guid? Location)>> LiveAsync(AccessWorld w, Guid employeeId)
    {
        var db = w.Context(AccessWorld.TenantA);
        var roles = await db.AccessRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.TemplateKey ?? r.Name);
        return (await db.RoleAssignments.AsNoTracking().Where(a => a.EmployeeId == employeeId && a.RevokedAtUtc == null).ToListAsync())
            .Select(a => (roles[a.AccessRoleId], a.Scope, a.LocationId))
            .OrderBy(x => x.Item1).ThenBy(x => x.LocationId)
            .ToList();
    }

    [Fact]
    public async Task An_admin_holds_company_administrator_company_wide()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        Assert.Equal([(AccessRoleTemplates.CompanyAdmin, AccessScopeType.Company, (Guid?)null)], await LiveAsync(w, w.Admin1));
    }

    [Fact]
    public async Task A_manager_holds_the_legacy_role_at_each_managed_branch_and_the_pin_helper_company_wide()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        var live = await LiveAsync(w, w.M1);

        Assert.Equal(3, live.Count);
        Assert.Contains((AccessRoleTemplates.LegacyManager, AccessScopeType.Branch, (Guid?)w.B1), live);
        Assert.Contains((AccessRoleTemplates.LegacyManager, AccessScopeType.Branch, (Guid?)w.B2), live);
        Assert.Contains((AccessRoleTemplates.PinHelper, AccessScopeType.Company, (Guid?)null), live);
    }

    [Fact]
    public async Task Plain_staff_and_the_operator_row_hold_nothing()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        Assert.Empty(await LiveAsync(w, w.E1));
        Assert.Empty(await LiveAsync(w, w.Operator));
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        Assert.Equal(0, await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA));
    }

    [Fact]
    public async Task Drift_heals_a_branch_taken_away_a_promotion_and_a_demotion()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.ManagedLocations.Remove(db.ManagedLocations.Single(m => m.EmployeeId == w.M1 && m.LocationId == w.B2));
            db.Employees.Single(e => e.Id == w.E1).Role = EmployeeRole.Admin;
            db.Employees.Single(e => e.Id == w.M2).Role = EmployeeRole.Employee;
            db.SaveChanges();
        }

        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        Assert.DoesNotContain((AccessRoleTemplates.LegacyManager, AccessScopeType.Branch, (Guid?)w.B2), await LiveAsync(w, w.M1));
        Assert.Equal([(AccessRoleTemplates.CompanyAdmin, AccessScopeType.Company, (Guid?)null)], await LiveAsync(w, w.E1));
        Assert.Empty(await LiveAsync(w, w.M2));
    }

    [Fact]
    public async Task What_a_person_assigned_is_never_touched()
    {
        using var w = new AccessWorld();
        var db = w.Context(AccessWorld.TenantA);
        var roles = await AccessSeeder.EnsureTemplatesAsync(db, AccessWorld.TenantA);
        db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = w.E3, AccessRoleId = roles[AccessRoleTemplates.Observer].Id,
            Scope = AccessScopeType.Branch, LocationId = w.B3, CreatedByEmployeeId = w.Admin1,
        });
        await db.SaveChangesAsync();

        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        Assert.Equal([(AccessRoleTemplates.Observer, AccessScopeType.Branch, (Guid?)w.B3)], await LiveAsync(w, w.E3));
    }

    [Fact]
    public async Task A_row_a_person_made_stands_in_for_the_one_the_system_would_add()
    {
        // Staging, 2026-10-15: a manager made admin by hand, later made Admin on the employee form. The system
        // added its own admin row beside the person's; Postgres refused a second active row for one key and the
        // company's whole reconcile failed — on every start.
        using var w = new AccessWorld();
        var db = w.Context(AccessWorld.TenantA);
        await AccessSeeder.ReconcileTenantAsync(db, AccessWorld.TenantA);
        var adminRole = db.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.CompanyAdmin).Id;
        var byHand = new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = w.M3, AccessRoleId = adminRole, Scope = AccessScopeType.Company,
            CreatedByEmployeeId = w.Admin1,
        };
        db.RoleAssignments.Add(byHand);
        db.Employees.Single(e => e.Id == w.M3).Role = EmployeeRole.Admin;
        await db.SaveChangesAsync();

        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        Assert.Equal([(AccessRoleTemplates.CompanyAdmin, AccessScopeType.Company, (Guid?)null)], await LiveAsync(w, w.M3));
        Assert.Equal(byHand.Id, w.Context(AccessWorld.TenantA).RoleAssignments.AsNoTracking()
            .Single(a => a.EmployeeId == w.M3 && a.RevokedAtUtc == null).Id);
        Assert.Equal(0, await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA));

        // Taken away by a person later: the system's own row takes its place.
        using (var edit = w.Context(AccessWorld.TenantA))
        {
            edit.RoleAssignments.Single(a => a.Id == byHand.Id).RevokedAtUtc = DateTime.UtcNow;
            edit.SaveChanges();
        }
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        var system = w.Context(AccessWorld.TenantA).RoleAssignments.AsNoTracking().Single(a => a.EmployeeId == w.M3 && a.RevokedAtUtc == null);
        Assert.Equal((adminRole, (Guid?)null), (system.AccessRoleId, system.CreatedByEmployeeId));
    }

    [Fact]
    public async Task One_company_that_fails_does_not_stop_the_others()
    {
        // Staging, 2026-10-15: one company's bad row threw out of the start-up loop and every company after it
        // stayed unreconciled. The companies share one context, so the failed company's unsaved rows must go too,
        // or the next company's save trips over them.
        using var w = new AccessWorld();
        var refuse = new RefuseSavesFor(AccessWorld.TenantA);
        var db = w.Context(AccessWorld.TenantA, interceptor: refuse);

        await AccessSeeder.ReconcileAllAsync(db);

        Assert.True(refuse.Refused > 0);
        Assert.Empty(w.Context(AccessWorld.TenantA).RoleAssignments.AsNoTracking());
        Assert.NotEmpty(w.Context(AccessWorld.TenantB).RoleAssignments.AsNoTracking());
        Assert.DoesNotContain(db.ChangeTracker.Entries(), e => e.State is EntityState.Added or EntityState.Modified);
    }

    [Fact]
    public async Task A_reconcile_that_fails_after_a_write_leaves_nothing_for_the_requests_next_save()
    {
        using var w = new AccessWorld();
        var refuse = new RefuseSavesFor(AccessWorld.TenantA);
        var db = w.Context(AccessWorld.TenantA, interceptor: refuse);
        db.Employees.Single(e => e.Id == w.E1).FullName = "Yeni ad";
        await db.SaveChangesAsync(); // the request's own write

        await AccessSeeder.ReconcileAfterWriteAsync(db, AccessWorld.TenantA, logger: null);

        Assert.True(refuse.Refused > 0);
        db.Employees.Single(e => e.Id == w.E2).FullName = "Daha bir ad";
        await db.SaveChangesAsync(); // the request goes on — this save must not retry the reconcile's rows
        Assert.Equal("Daha bir ad", w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(e => e.Id == w.E2).FullName);
    }

    /// <summary>Refuses any save that would add an assignment for one company — a failure at save time, where
    /// Postgres raises its 23505 or 23503 and the in-memory store never would.</summary>
    private sealed class RefuseSavesFor(Guid tenantId) : SaveChangesInterceptor
    {
        public int Refused { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Check(eventData.Context!);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Check(eventData.Context!);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Check(DbContext db)
        {
            if (!db.ChangeTracker.Entries<RoleAssignment>().Any(e => e.State == EntityState.Added && e.Entity.TenantId == tenantId))
                return;
            Refused++;
            throw new DbUpdateException("refused by the test");
        }
    }

    [Fact]
    public async Task A_locked_role_edited_by_hand_is_put_back_and_an_editable_one_is_left_alone()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.LegacyManager).Permissions.Add(AccessPermissions.EmployeesDelete);
            db.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.Timekeeper).Permissions.Add(AccessPermissions.AttendanceCorrect);
            db.SaveChanges();
        }

        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        var after = w.Context(AccessWorld.TenantA).AccessRoles.AsNoTracking().ToList();
        Assert.DoesNotContain(AccessPermissions.EmployeesDelete, after.Single(r => r.TemplateKey == AccessRoleTemplates.LegacyManager).Permissions);
        Assert.Contains(AccessPermissions.AttendanceCorrect, after.Single(r => r.TemplateKey == AccessRoleTemplates.Timekeeper).Permissions);
    }

    [Fact]
    public async Task Reconciling_one_company_never_touches_another()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantA), AccessWorld.TenantA);

        var b = w.Context(AccessWorld.TenantB);
        Assert.Empty(b.AccessRoles.AsNoTracking());
        Assert.Empty(b.RoleAssignments.AsNoTracking());
        Assert.Equal(6, w.Context(AccessWorld.TenantA).AccessRoles.Count());
    }
}
