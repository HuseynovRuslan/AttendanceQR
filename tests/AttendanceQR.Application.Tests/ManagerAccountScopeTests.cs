using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Regression tests for the manager account-management boundary (P0). ScopedEmployeeAsync used to
/// check only branch membership, and admins/managers also carry a LocationId (where they clock in) —
/// so a manager could edit a same-branch admin's email/phone or reset their PIN and read the temp
/// PIN: an account takeover, and with it role escalation. The rule now lives in ONE place
/// (ManageableEmployeeAsync): a manager may act only on a Role==Employee target inside a branch they
/// oversee, in their own tenant. Same-branch admin/manager/self → 403 with no state touched; other
/// branch or other tenant → 404 (no probing). If any of these tests fail, that boundary regressed.
/// </summary>
public class ManagerAccountScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ManagerController Controller { get; }
        public Guid BranchA { get; } = Guid.NewGuid();   // managed by the manager
        public Guid BranchB { get; } = Guid.NewGuid();   // same tenant, NOT managed
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid SameBranchManagerId { get; } = Guid.NewGuid();
        public Guid OtherBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid OtherTenantEmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            // The controller runs as tenant A — the global query filter on Employees scopes every
            // lookup to it, which is what the cross-tenant tests rely on.
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"mgr-scope-{Guid.NewGuid()}")
                .Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            Db.Locations.Add(Location(BranchA, "Filial A"));
            Db.Locations.Add(Location(BranchB, "Filial B"));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantA });

            Db.Employees.Add(Person(ManagerId, "Menecer Özü", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Filial İşçisi", EmployeeRole.Employee, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchAdminId, "Filial Admini", EmployeeRole.Admin, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchManagerId, "İkinci Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Başqa Filial", EmployeeRole.Employee, BranchB, TenantA));
            // Deliberately placed at BranchA's OWN location id: only the tenant filter hides them,
            // which is exactly the boundary this row tests.
            Db.Employees.Add(Person(OtherTenantEmployeeId, "Başqa Tenant", EmployeeRole.Employee, BranchA, TenantB));
            Db.SaveChanges();

            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", ManagerId.ToString()),
                new Claim("role", nameof(EmployeeRole.Manager)),
            }, "test");
            Controller = new ManagerController(Db, new StubHasher(), new StubSummary())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        private static Location Location(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantA, Name = name,
            Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId, Guid tenantId) => new()
        {
            Id = id, TenantId = tenantId, FullName = name, Role = role, LocationId = locationId,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "original-hash",
        };

        public Employee Row(Guid id) => Db.Employees.IgnoreQueryFilters().AsNoTracking().Single(e => e.Id == id);

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>A minimal legal edit body targeting the managed branch.</summary>
    private static ManagerEmployeeRequest Edit(string fullName, Guid locationId) => new(
        FullName: fullName, Email: null, PhoneNumber: null, FatherName: null, Position: null,
        LocationId: locationId);

    private static void AssertForbidden(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    // --- profile edit (PUT /api/manager/employees/{id}) -------------------------

    [Fact]
    public async Task Update_same_branch_employee_is_allowed()
    {
        using var h = new Harness();
        var result = await h.Controller.UpdateEmployee(h.SameBranchEmployeeId, Edit("Yeni Ad", h.BranchA));
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Yeni Ad", h.Row(h.SameBranchEmployeeId).FullName);
    }

    [Fact]
    public async Task Update_same_branch_admin_is_forbidden_and_untouched()
    {
        using var h = new Harness();
        var result = await h.Controller.UpdateEmployee(h.SameBranchAdminId, Edit("Ele Keçirilmiş", h.BranchA));
        AssertForbidden(result);
        Assert.Equal("Filial Admini", h.Row(h.SameBranchAdminId).FullName);
    }

    [Fact]
    public async Task Update_same_branch_manager_is_forbidden()
    {
        using var h = new Harness();
        var result = await h.Controller.UpdateEmployee(h.SameBranchManagerId, Edit("Ele Keçirilmiş", h.BranchA));
        AssertForbidden(result);
        Assert.Equal("İkinci Menecer", h.Row(h.SameBranchManagerId).FullName);
    }

    [Fact]
    public async Task Update_own_account_is_forbidden()
    {
        using var h = new Harness();
        var result = await h.Controller.UpdateEmployee(h.ManagerId, Edit("Özünü Dəyişən", h.BranchA));
        AssertForbidden(result);
        Assert.Equal("Menecer Özü", h.Row(h.ManagerId).FullName);
    }

    [Fact]
    public async Task Update_other_branch_employee_is_not_found()
    {
        using var h = new Harness();
        var result = await h.Controller.UpdateEmployee(h.OtherBranchEmployeeId, Edit("Yad Filial", h.BranchA));
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Başqa Filial", h.Row(h.OtherBranchEmployeeId).FullName);
    }

    [Fact]
    public async Task Update_other_tenant_employee_is_not_found()
    {
        using var h = new Harness();
        var result = await h.Controller.UpdateEmployee(h.OtherTenantEmployeeId, Edit("Yad Tenant", h.BranchA));
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("Başqa Tenant", h.Row(h.OtherTenantEmployeeId).FullName);
    }

    // --- PIN reset (POST /api/manager/employees/{id}/reset-pin) -----------------

    [Fact]
    public async Task ResetPin_same_branch_employee_is_allowed_and_rotates_credentials()
    {
        using var h = new Harness();
        var result = await h.Controller.ResetPin(h.SameBranchEmployeeId);
        Assert.IsType<OkObjectResult>(result);
        var row = h.Row(h.SameBranchEmployeeId);
        Assert.NotEqual("original-hash", row.PasswordHash);
        Assert.True(row.MustChangePin);
        Assert.Equal(1, row.TokenVersion); // old sessions are ended
    }

    [Fact]
    public async Task ResetPin_same_branch_admin_is_forbidden_and_leaks_nothing()
    {
        using var h = new Harness();
        var result = await h.Controller.ResetPin(h.SameBranchAdminId);
        AssertForbidden(result);
        // The takeover vector: no temp PIN in the response body, and the credentials never moved.
        var body = ((ObjectResult)result).Value?.ToString() ?? "";
        Assert.DoesNotContain("tempPin", body, StringComparison.OrdinalIgnoreCase);
        var row = h.Row(h.SameBranchAdminId);
        Assert.Equal("original-hash", row.PasswordHash);
        Assert.Equal(0, row.TokenVersion);
    }

    [Fact]
    public async Task ResetPin_same_branch_manager_is_forbidden()
    {
        using var h = new Harness();
        var result = await h.Controller.ResetPin(h.SameBranchManagerId);
        AssertForbidden(result);
        Assert.Equal("original-hash", h.Row(h.SameBranchManagerId).PasswordHash);
    }

    [Fact]
    public async Task ResetPin_own_account_is_forbidden()
    {
        using var h = new Harness();
        var result = await h.Controller.ResetPin(h.ManagerId);
        AssertForbidden(result);
        Assert.Equal("original-hash", h.Row(h.ManagerId).PasswordHash);
    }

    [Fact]
    public async Task ResetPin_other_branch_employee_is_not_found()
    {
        using var h = new Harness();
        var result = await h.Controller.ResetPin(h.OtherBranchEmployeeId);
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("original-hash", h.Row(h.OtherBranchEmployeeId).PasswordHash);
    }

    [Fact]
    public async Task ResetPin_other_tenant_employee_is_not_found()
    {
        using var h = new Harness();
        var result = await h.Controller.ResetPin(h.OtherTenantEmployeeId);
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("original-hash", h.Row(h.OtherTenantEmployeeId).PasswordHash);
    }

    // --- the same central rule guards the remaining per-account writes -----------

    [Fact]
    public async Task CreateLeave_for_same_branch_admin_is_forbidden()
    {
        using var h = new Harness();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await h.Controller.CreateLeave(
            new LeaveRecordRequest(h.SameBranchAdminId, today, today, LeaveType.Vacation, null));
        AssertForbidden(result);
        Assert.Empty(h.Db.LeaveRecords.IgnoreQueryFilters().ToList());
    }

    [Fact]
    public async Task Leaves_list_excludes_same_branch_admin_records()
    {
        using var h = new Harness();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        h.Db.LeaveRecords.Add(new LeaveRecord
        {
            EmployeeId = h.SameBranchAdminId, FromDate = today, ToDate = today,
            Type = LeaveType.Vacation, CreatedByEmployeeId = h.SameBranchAdminId,
        });
        h.Db.LeaveRecords.Add(new LeaveRecord
        {
            EmployeeId = h.SameBranchEmployeeId, FromDate = today, ToDate = today,
            Type = LeaveType.Vacation, CreatedByEmployeeId = h.ManagerId,
        });
        h.Db.SaveChanges();

        var ok = Assert.IsType<OkObjectResult>(await h.Controller.Leaves(null, null));
        var ids = ((System.Collections.IEnumerable)ok.Value!).Cast<object>()
            .Select(r => (Guid)r.GetType().GetProperty("employeeId")!.GetValue(r)!)
            .ToList();
        Assert.Equal(new[] { h.SameBranchEmployeeId }, ids);
    }

    // --- schedules: the indirect write (shift hours re-judge pay for everyone on the shift) ---------

    [Fact]
    public async Task UpdateSchedule_with_same_branch_admin_on_it_is_forbidden()
    {
        using var h = new Harness();
        var schedule = new Schedule
        {
            Name = "Gündüz", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 126,
        };
        h.Db.Schedules.Add(schedule);
        h.Db.Employees.Single(e => e.Id == h.SameBranchAdminId).ScheduleId = schedule.Id;
        h.Db.SaveChanges();

        var result = await h.Controller.UpdateSchedule(schedule.Id, new ScheduleRequest("Gündüz", "08:00", "17:00"));
        AssertForbidden(result);
        Assert.Equal(new TimeOnly(9, 0), h.Db.Schedules.AsNoTracking().Single(s => s.Id == schedule.Id).ShiftStart);
    }

    [Fact]
    public async Task UpdateSchedule_with_only_managed_employees_is_allowed()
    {
        using var h = new Harness();
        var schedule = new Schedule
        {
            Name = "Gündüz", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 126,
        };
        h.Db.Schedules.Add(schedule);
        h.Db.Employees.Single(e => e.Id == h.SameBranchEmployeeId).ScheduleId = schedule.Id;
        h.Db.SaveChanges();

        var result = await h.Controller.UpdateSchedule(schedule.Id, new ScheduleRequest("Gündüz", "08:00", "17:00"));
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(new TimeOnly(8, 0), h.Db.Schedules.AsNoTracking().Single(s => s.Id == schedule.Id).ShiftStart);
    }

    [Fact]
    public async Task Employee_list_contains_only_role_employee()
    {
        using var h = new Harness();
        var result = Assert.IsType<OkObjectResult>(await h.Controller.Employees());
        var ids = ((System.Collections.IEnumerable)result.Value!).Cast<object>()
            .Select(r => (Guid)r.GetType().GetProperty("id")!.GetValue(r)!)
            .ToList();
        Assert.Equal(new[] { h.SameBranchEmployeeId }, ids);
    }
}
