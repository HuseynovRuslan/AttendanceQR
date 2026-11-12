using System.Collections;
using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Access;
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
/// Pins the manager boundary on the field-visit surface — the same P0 rule as
/// <see cref="ManagerAccountScopeTests"/>, enforced via <c>LocationScopeRules.CanManageEmployeeAsync</c>:
/// a Manager may assign field visits (and see people on the assignable list) only for Role==Employee
/// workers in their managed branches, and an Admin who clocks in at the branch is out of reach.
/// Reading (the day's board, a visit's checklist and work photo) is LocationScopeRules.FieldVisitReach, the
/// owner's hierarchy of 2026-11-12: a manager reads the plain staff at their branches; a Structure Manager (the
/// structure_operator role over branches) also reads the Timekeepers there — the branch managers who are not
/// Structure Managers themselves; nobody below the company reads an Admin's visits. An Admin caller keeps the
/// whole tenant.
/// </summary>
public class FieldVisitScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public FieldVisitController AsManager { get; }
        public FieldVisitController AsAdmin { get; }
        public FieldVisitController AsStructureManager { get; }
        public Guid StructureManagerId { get; } = Guid.NewGuid();   // a manager of BranchA who may become a Structure Manager
        public Guid BranchA { get; } = Guid.NewGuid();   // managed
        public Guid BranchB { get; } = Guid.NewGuid();   // same tenant, not managed
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid SameBranchManagerId { get; } = Guid.NewGuid();
        public Guid OtherBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid OtherTenantEmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fv-scope-{Guid.NewGuid()}")
                .Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantA });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = StructureManagerId, LocationId = BranchA, TenantId = TenantA });

            // Everyone gets CanFieldCheckIn so a 403 in these tests can ONLY come from the manage rule,
            // never from the separate NotFieldWorker gate.
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Filial İşçisi", EmployeeRole.Employee, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchAdminId, "Filial Admini", EmployeeRole.Admin, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchManagerId, "İkinci Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Başqa Filial", EmployeeRole.Employee, BranchB, TenantA));
            Db.Employees.Add(Person(OtherTenantEmployeeId, "Başqa Tenant", EmployeeRole.Employee, BranchA, TenantB));
            Db.Employees.Add(Person(StructureManagerId, "Struktur Meneceri", EmployeeRole.Manager, BranchA, TenantA));
            Db.SaveChanges();

            AsManager = Controller(ManagerId, EmployeeRole.Manager);
            AsAdmin = Controller(SameBranchAdminId, EmployeeRole.Admin);
            AsStructureManager = Controller(StructureManagerId, EmployeeRole.Manager);
        }

        /// <summary>Gives <paramref name="employeeId"/> the structure_operator role over these branches — what makes a
        /// Structure Manager. Nothing else about the person changes: they stay Role == Manager.</summary>
        public void GrantStructureManager(Guid employeeId, bool fieldVisitsView = true, bool revoked = false, params Guid[] branches)
        {
            var role = new AccessRole
            {
                Id = Guid.NewGuid(), TenantId = TenantA, Name = "Struktur operatoru", TemplateKey = AccessRoleTemplates.StructureOperator,
                Permissions = fieldVisitsView
                    ? [AccessPermissions.FieldVisitsView, AccessPermissions.EmployeesView]
                    : [AccessPermissions.EmployeesView],
                CreatedAtUtc = DateTime.UtcNow,
            };
            Db.AccessRoles.Add(role);
            foreach (var b in branches)
                Db.RoleAssignments.Add(new RoleAssignment
                {
                    Id = Guid.NewGuid(), TenantId = TenantA, EmployeeId = employeeId, AccessRoleId = role.Id,
                    Scope = AccessScopeType.Branch, LocationId = b, CreatedAtUtc = DateTime.UtcNow,
                    RevokedAtUtc = revoked ? DateTime.UtcNow : null,
                });
            Db.SaveChanges();
        }

        private FieldVisitController Controller(Guid callerId, EmployeeRole role)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", callerId.ToString()),
                new Claim("role", role.ToString()),
            }, "test");
            return new FieldVisitController(Db, new StubPhoto(), new StubPush(), new AppOptions { TimeZone = "Asia/Baku" },
                new StubSummaries(), FieldTestLog.Logger)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId, Guid tenantId) => new()
        {
            Id = id, TenantId = tenantId, FullName = name, Role = role, LocationId = locationId,
            CanFieldCheckIn = true, IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        public DateOnly Today() => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku")));

        public FieldVisit Visit(Guid employeeId, FieldVisitStatus status = FieldVisitStatus.Assigned)
        {
            var v = new FieldVisit
            {
                EmployeeId = employeeId, VisitDate = Today(), Status = status,
                AssignedByEmployeeId = SameBranchAdminId, AssignedAtUtc = DateTime.UtcNow, TenantId = TenantA,
            };
            Db.FieldVisits.Add(v);
            Db.SaveChanges();
            return v;
        }

        public int VisitCount() => Db.FieldVisits.IgnoreQueryFilters().Count();

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid employeeId, Guid recordId, byte[] webpBytes, CancellationToken ct = default) => Task.FromResult("key");
        public Task<string> UploadReferencePhotoAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default) => Task.FromResult("key");
        public Task<string> UploadAvatarAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default) => Task.FromResult("key");
        public Task<string> UploadFieldWorkPhotoAsync(Guid tenantId, Guid visitId, byte[] jpegBytes, CancellationToken ct = default) => Task.FromResult("fieldwork/key.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    private static void AssertForbidden(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    private static List<Guid> Ids(IActionResult result, string property)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((IEnumerable)ok.Value!).Cast<object>()
            .Select(r => (Guid)r.GetType().GetProperty(property)!.GetValue(r)!)
            .ToList();
    }

    // --- assign -----------------------------------------------------------------

    [Fact]
    public async Task Manager_can_assign_to_same_branch_employee()
    {
        using var h = new Harness();
        var result = await h.AsManager.Assign(new AssignFieldVisitRequest(h.SameBranchEmployeeId));
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, h.VisitCount());
    }

    [Fact]
    public async Task Manager_cannot_assign_to_same_branch_admin()
    {
        using var h = new Harness();
        AssertForbidden(await h.AsManager.Assign(new AssignFieldVisitRequest(h.SameBranchAdminId)));
        Assert.Equal(0, h.VisitCount());
    }

    [Fact]
    public async Task Manager_can_assign_to_a_fellow_manager()
    {
        // 2026-09-18, the owner's call: a fellow MANAGER is reachable company-wide (IsColleague).
        using var h = new Harness();
        Assert.IsType<OkObjectResult>(await h.AsManager.Assign(new AssignFieldVisitRequest(h.SameBranchManagerId)));
        Assert.Equal(1, h.VisitCount());
    }

    [Fact]
    public async Task Manager_cannot_assign_to_other_branch_employee()
    {
        using var h = new Harness();
        AssertForbidden(await h.AsManager.Assign(new AssignFieldVisitRequest(h.OtherBranchEmployeeId)));
        Assert.Equal(0, h.VisitCount());
    }

    [Fact]
    public async Task Manager_cannot_assign_to_other_tenant_employee()
    {
        using var h = new Harness();
        // The tenant filter hides the row entirely — the worker lookup fails before scope is judged.
        Assert.IsType<BadRequestObjectResult>(await h.AsManager.Assign(new AssignFieldVisitRequest(h.OtherTenantEmployeeId)));
        Assert.Equal(0, h.VisitCount());
    }

    [Fact]
    public async Task Admin_can_still_assign_to_a_manager()
    {
        using var h = new Harness();
        var result = await h.AsAdmin.Assign(new AssignFieldVisitRequest(h.SameBranchManagerId));
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, h.VisitCount());
    }

    // --- cancel -----------------------------------------------------------------

    [Fact]
    public async Task Manager_cannot_cancel_an_admins_visit()
    {
        using var h = new Harness();
        var visit = h.Visit(h.SameBranchAdminId);
        AssertForbidden(await h.AsManager.Cancel(visit.Id));
        Assert.Equal(FieldVisitStatus.Assigned, h.Db.FieldVisits.IgnoreQueryFilters().AsNoTracking().Single(v => v.Id == visit.Id).Status);
    }

    [Fact]
    public async Task Manager_can_cancel_their_employees_visit()
    {
        using var h = new Harness();
        var visit = h.Visit(h.SameBranchEmployeeId);
        Assert.IsType<OkObjectResult>(await h.AsManager.Cancel(visit.Id));
    }

    // --- board + assignable listings --------------------------------------------

    [Fact]
    public async Task Plain_manager_board_shows_the_staff_not_a_fellow_manager_or_an_admin()
    {
        // Two managers sharing a branch do not get each other's field days: that reach is the Structure Manager's.
        using var h = new Harness();
        h.Visit(h.SameBranchEmployeeId);
        h.Visit(h.SameBranchAdminId);
        h.Visit(h.SameBranchManagerId);

        var ids = Ids(await h.AsManager.Board(h.Today()), "employeeId");
        Assert.Equal(new[] { h.SameBranchEmployeeId }, ids);

        // An admin keeps the full picture.
        Assert.Equal(3, Ids(await h.AsAdmin.Board(h.Today()), "employeeId").Count);
    }

    [Fact]
    public async Task Manager_board_still_hides_other_branches_and_other_tenants()
    {
        using var h = new Harness();
        h.Visit(h.OtherBranchEmployeeId);
        h.Visit(h.OtherTenantEmployeeId);

        Assert.Empty(Ids(await h.AsManager.Board(h.Today()), "employeeId"));
    }

    [Fact]
    public async Task Plain_manager_cannot_read_a_fellow_managers_checklist_or_work_photo()
    {
        // The detail follows the board: what is not on it does not open by id either.
        using var h = new Harness();
        var visit = h.Visit(h.SameBranchManagerId);
        AssertForbidden(await h.AsManager.Checklist(visit.Id));
        AssertForbidden(await h.AsManager.WorkPhoto(visit.Id));
    }

    // --- Structure Manager --------------------------------------------------------

    [Fact]
    public async Task Structure_manager_board_shows_staff_and_timekeepers_of_their_branches()
    {
        using var h = new Harness();
        h.GrantStructureManager(h.StructureManagerId, branches: h.BranchA);
        h.Visit(h.SameBranchEmployeeId);
        h.Visit(h.SameBranchManagerId);   // a Timekeeper: Role == Manager, not a Structure Manager
        h.Visit(h.ManagerId);             // another Timekeeper at the same branch
        h.Visit(h.SameBranchAdminId);     // never
        h.Visit(h.OtherBranchEmployeeId); // not their branch
        h.Visit(h.OtherTenantEmployeeId); // not their company

        var ids = Ids(await h.AsStructureManager.Board(h.Today()), "employeeId");
        Assert.Equal(new[] { h.SameBranchEmployeeId, h.SameBranchManagerId, h.ManagerId }.OrderBy(x => x), ids.OrderBy(x => x));
    }

    [Fact]
    public async Task Structure_manager_reads_a_timekeepers_checklist_and_work_photo_but_not_an_admins()
    {
        using var h = new Harness();
        h.GrantStructureManager(h.StructureManagerId, branches: h.BranchA);
        var timekeeper = h.Visit(h.SameBranchManagerId);
        var admin = h.Visit(h.SameBranchAdminId);

        Assert.IsType<OkObjectResult>(await h.AsStructureManager.Checklist(timekeeper.Id));
        Assert.IsType<OkObjectResult>(await h.AsStructureManager.WorkPhoto(timekeeper.Id));
        AssertForbidden(await h.AsStructureManager.Checklist(admin.Id));
        AssertForbidden(await h.AsStructureManager.WorkPhoto(admin.Id));
    }

    [Fact]
    public async Task Structure_manager_does_not_see_another_structure_managers_visits()
    {
        using var h = new Harness();
        h.GrantStructureManager(h.StructureManagerId, branches: h.BranchA);
        h.GrantStructureManager(h.SameBranchManagerId, branches: h.BranchA);   // a peer, not a Timekeeper
        var peer = h.Visit(h.SameBranchManagerId);

        Assert.Empty(Ids(await h.AsStructureManager.Board(h.Today()), "employeeId"));
        AssertForbidden(await h.AsStructureManager.Checklist(peer.Id));
    }

    [Fact]
    public async Task Structure_manager_reach_is_the_roles_branches_not_more()
    {
        // The role over BranchB only, and no managed branch: BranchB's staff come into reach; BranchA's managers do
        // not become their Timekeepers.
        using var h = new Harness();
        h.Db.ManagedLocations.RemoveRange(h.Db.ManagedLocations.Where(m => m.EmployeeId == h.StructureManagerId));
        h.Db.SaveChanges();
        h.GrantStructureManager(h.StructureManagerId, branches: h.BranchB);
        h.Visit(h.OtherBranchEmployeeId);
        h.Visit(h.SameBranchManagerId);

        Assert.Equal(new[] { h.OtherBranchEmployeeId }, Ids(await h.AsStructureManager.Board(h.Today()), "employeeId"));
    }

    [Fact]
    public async Task The_role_without_field_visit_rights_or_revoked_gives_no_timekeeper_reach()
    {
        using (var h = new Harness())
        {
            h.GrantStructureManager(h.StructureManagerId, fieldVisitsView: false, branches: h.BranchA);
            h.Visit(h.SameBranchManagerId);
            Assert.Empty(Ids(await h.AsStructureManager.Board(h.Today()), "employeeId"));
        }
        using (var h = new Harness())
        {
            h.GrantStructureManager(h.StructureManagerId, revoked: true, branches: h.BranchA);
            h.Visit(h.SameBranchManagerId);
            Assert.Empty(Ids(await h.AsStructureManager.Board(h.Today()), "employeeId"));
        }
    }

    [Fact]
    public async Task Writing_is_unchanged_by_the_reading_rule()
    {
        // Acting still asks CanManageEmployeeAsync, exactly as before: the owner's 2026-09-18 reach to a fellow
        // manager for assigning stays, and an admin's visit stays out of reach for everybody below the company.
        using var h = new Harness();
        Assert.IsType<OkObjectResult>(await h.AsManager.Assign(new AssignFieldVisitRequest(h.SameBranchManagerId)));
        h.GrantStructureManager(h.StructureManagerId, branches: h.BranchA);
        var admin = h.Visit(h.SameBranchAdminId);
        AssertForbidden(await h.AsStructureManager.Cancel(admin.Id));
    }

    [Fact]
    public async Task Manager_board_still_shows_their_own_visits()
    {
        using var h = new Harness();
        h.Visit(h.ManagerId);            // the manager's own field day — theirs to see
        h.Visit(h.SameBranchAdminId);    // still hidden

        var ids = Ids(await h.AsManager.Board(h.Today()), "employeeId");
        Assert.Equal(new[] { h.ManagerId }, ids);
    }

    [Fact]
    public async Task Manager_assignable_list_is_role_employee_only()
    {
        using var h = new Harness();
        var ids = Ids(await h.AsManager.Assignable(), "id");
        Assert.Equal(new[] { h.SameBranchEmployeeId }, ids);
    }
}
