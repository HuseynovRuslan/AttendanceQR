using System.Collections;
using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
/// a Manager may assign/cancel field visits (and appear/see people on the board and assignable list)
/// only for Role==Employee workers in their managed branches. Admins and fellow managers who merely
/// clock in at the branch are out of reach; an Admin caller keeps the whole tenant.
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

            // Everyone gets CanFieldCheckIn so a 403 in these tests can ONLY come from the manage rule,
            // never from the separate NotFieldWorker gate.
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Filial İşçisi", EmployeeRole.Employee, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchAdminId, "Filial Admini", EmployeeRole.Admin, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchManagerId, "İkinci Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Başqa Filial", EmployeeRole.Employee, BranchB, TenantA));
            Db.Employees.Add(Person(OtherTenantEmployeeId, "Başqa Tenant", EmployeeRole.Employee, BranchA, TenantB));
            Db.SaveChanges();

            AsManager = Controller(ManagerId, EmployeeRole.Manager);
            AsAdmin = Controller(SameBranchAdminId, EmployeeRole.Admin);
        }

        private FieldVisitController Controller(Guid callerId, EmployeeRole role)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", callerId.ToString()),
                new Claim("role", role.ToString()),
            }, "test");
            return new FieldVisitController(Db, new StubPhoto(), new StubPush(), new AppOptions { TimeZone = "Asia/Baku" })
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
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
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
    public async Task Manager_cannot_assign_to_same_branch_manager()
    {
        using var h = new Harness();
        AssertForbidden(await h.AsManager.Assign(new AssignFieldVisitRequest(h.SameBranchManagerId)));
        Assert.Equal(0, h.VisitCount());
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
    public async Task Manager_board_excludes_admin_and_manager_visits()
    {
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
