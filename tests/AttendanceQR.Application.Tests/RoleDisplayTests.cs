using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The names roles are SHOWN under (RoleDisplay) — the owner's hierarchy of 2026-11-12, Company Admin → Structure
/// Manager → Timekeeper → Employee, read from the data and never from a person. Display only: none of these tests
/// is about a right.
/// </summary>
public class RoleDisplayTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000d1a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-00000000d1b2");

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid Branch1 { get; } = Guid.NewGuid();
        public Guid Branch2 { get; } = Guid.NewGuid();
        public Guid Outside { get; } = Guid.NewGuid();

        public World()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"role-display-{Guid.NewGuid()}").Options, tenant);
        }

        public Guid Person(EmployeeRole role, Guid? tenant = null, params Guid[] managed)
        {
            var id = Guid.NewGuid();
            var t = tenant ?? TenantA;
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = t, FullName = "P " + id.ToString()[..4], Role = role, IsActive = true, PasswordHash = "h",
                LocationId = managed.Length > 0 ? managed[0] : Branch1, CreatedAtUtc = DateTime.UtcNow, ActivatedAtUtc = DateTime.UtcNow,
            });
            foreach (var b in managed)
                Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = id, LocationId = b, TenantId = t });
            Db.SaveChanges();
            return id;
        }

        public void Grant(Guid employeeId, string template, Guid? tenant = null, params Guid[] branches)
        {
            var t = tenant ?? TenantA;
            var role = new AccessRole
            {
                Id = Guid.NewGuid(), TenantId = t, Name = template, TemplateKey = template,
                Permissions = [AccessPermissions.EmployeesView], CreatedAtUtc = DateTime.UtcNow,
            };
            Db.AccessRoles.Add(role);
            foreach (var b in branches)
                Db.RoleAssignments.Add(new RoleAssignment
                {
                    Id = Guid.NewGuid(), TenantId = t, EmployeeId = employeeId, AccessRoleId = role.Id,
                    Scope = AccessScopeType.Branch, LocationId = b, CreatedAtUtc = DateTime.UtcNow,
                });
            Db.SaveChanges();
        }

        public Task<RoleDisplay.Labels> Labels() => RoleDisplay.LoadAsync(Db, default);

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task A_company_without_a_structure_manager_keeps_its_managers_title()
    {
        using var w = new World();
        var admin = w.Person(EmployeeRole.Admin);
        var manager = w.Person(EmployeeRole.Manager, null, w.Branch1);
        var worker = w.Person(EmployeeRole.Employee);

        var labels = await w.Labels();
        Assert.False(labels.Hierarchy);
        Assert.Equal(RoleDisplay.CompanyAdmin, labels.For(admin, EmployeeRole.Admin));
        Assert.Equal(RoleDisplay.Manager, labels.For(manager, EmployeeRole.Manager));
        Assert.Equal(RoleDisplay.Employee, labels.For(worker, EmployeeRole.Employee));
    }

    [Fact]
    public async Task Under_a_structure_manager_the_managers_of_their_branches_are_timekeepers()
    {
        using var w = new World();
        var sm = w.Person(EmployeeRole.Manager, null, w.Branch1, w.Branch2);
        w.Grant(sm, AccessRoleTemplates.StructureOperator, null, w.Branch1, w.Branch2);
        var inside = w.Person(EmployeeRole.Manager, null, w.Branch1);
        var straddling = w.Person(EmployeeRole.Manager, null, w.Branch2, w.Outside);   // one branch outside the reach
        var unassigned = w.Person(EmployeeRole.Manager);                                 // manages nothing yet

        var labels = await w.Labels();
        Assert.True(labels.Hierarchy);
        Assert.Equal(RoleDisplay.StructureManager, labels.For(sm, EmployeeRole.Manager));
        Assert.Equal(RoleDisplay.Timekeeper, labels.For(inside, EmployeeRole.Manager));
        Assert.Equal(RoleDisplay.Manager, labels.For(straddling, EmployeeRole.Manager));
        Assert.Equal(RoleDisplay.Manager, labels.For(unassigned, EmployeeRole.Manager));
    }

    [Fact]
    public async Task A_timekeeper_role_shows_as_timekeeper_even_on_plain_staff()
    {
        using var w = new World();
        var worker = w.Person(EmployeeRole.Employee);
        w.Grant(worker, AccessRoleTemplates.Timekeeper, null, w.Branch1);

        Assert.Equal(RoleDisplay.Timekeeper, (await w.Labels()).For(worker, EmployeeRole.Employee));
    }

    [Fact]
    public async Task Plain_staff_are_not_listed_and_another_company_changes_nothing()
    {
        using var w = new World();
        var worker = w.Person(EmployeeRole.Employee);
        var manager = w.Person(EmployeeRole.Manager, null, w.Branch1);
        var foreignSm = w.Person(EmployeeRole.Manager, TenantB, w.Branch1);
        w.Grant(foreignSm, AccessRoleTemplates.StructureOperator, TenantB, w.Branch1);   // same branch id, other company

        var labels = await w.Labels();
        Assert.DoesNotContain(worker, labels.ByEmployee.Keys);
        Assert.DoesNotContain(foreignSm, labels.ByEmployee.Keys);
        Assert.False(labels.Hierarchy);
        Assert.Equal(RoleDisplay.Manager, labels.For(manager, EmployeeRole.Manager));
    }
}
