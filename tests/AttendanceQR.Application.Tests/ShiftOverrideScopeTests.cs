using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Who may record a cover day.
///
/// Rostering deliberately uses the SEEING boundary, not the managing one. The Role==Employee rule in
/// CanManageEmployeeAsync guards the ACCOUNT — the PIN, the role, the phone somebody logs in with —
/// after a manager was found able to reset a same-branch admin's PIN and take the company. A cover
/// day touches none of that: it says which hours one day is measured by, and it carries the name of
/// whoever recorded it.
///
/// Applying the account rule here made the roster wrong instead of making anything safer. A site with
/// two managers covering each other's nights had no way to record that about the other, so those
/// nights were scored against the wrong shift — the very failure the override exists to prevent.
/// What is still refused is another branch entirely, and anyone who is not a manager.
/// </summary>
public class ShiftOverrideScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid BranchA { get; } = Guid.NewGuid();   // managed
        public Guid BranchB { get; } = Guid.NewGuid();   // same tenant, not managed
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid ColleagueManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid OtherBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid NightShiftId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"roster-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Locations.Add(Branch(BranchA, "Filial A"));
            Db.Locations.Add(Branch(BranchB, "Filial B"));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantA });
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, BranchA));
            Db.Employees.Add(Person(ColleagueManagerId, "Hemkar Menecer", EmployeeRole.Manager, BranchA));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Filial Iscisi", EmployeeRole.Employee, BranchA));
            Db.Employees.Add(Person(SameBranchAdminId, "Filial Admini", EmployeeRole.Admin, BranchA));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Basqa Filial", EmployeeRole.Employee, BranchB));
            Db.Schedules.Add(new Schedule
            {
                Id = NightShiftId, TenantId = TenantA, LocationId = BranchA, Name = "Gece novbesi",
                ShiftStart = new TimeOnly(20, 0), ShiftEnd = new TimeOnly(6, 0),
                WorkDaysMask = 127, LateThresholdMinutes = 15,
            });
            Db.SaveChanges();
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantA, Name = name,
            Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId) => new()
        {
            Id = id, TenantId = TenantA, FullName = name, Role = role, LocationId = locationId,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        public ShiftOverridesController As(Guid callerId, EmployeeRole role)
        {
            var identity = new ClaimsIdentity(
                [new Claim("sub", callerId.ToString()), new Claim("role", role.ToString())], "test");
            return new ShiftOverridesController(Db, new StubSummaries())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        public ShiftOverrideRequest Cover(Guid employeeId) =>
            new(employeeId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), NightShiftId, "geceni evez etdi");

        public void Dispose() => Db.Dispose();

        private sealed class StubSummaries : IDailySummaryService
        {
            public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
        }
    }

    private static bool Forbidden(IActionResult r) => r is ObjectResult { StatusCode: 403 };

    [Fact]
    public async Task Manager_can_roster_a_fellow_manager_at_their_own_branch()
    {
        // The case that sent us here: two managers at one site cover each other's nights, and neither
        // could record it about the other.
        using var h = new Harness();

        var result = await h.As(h.ManagerId, EmployeeRole.Manager).Set(h.Cover(h.ColleagueManagerId));

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(h.Db.ShiftOverrides.Where(o => o.EmployeeId == h.ColleagueManagerId));
    }

    [Fact]
    public async Task The_row_carries_the_name_of_whoever_recorded_it()
    {
        // The reason this can be opened up at all: a cover day is attributable, unlike a PIN reset.
        using var h = new Harness();

        await h.As(h.ManagerId, EmployeeRole.Manager).Set(h.Cover(h.ColleagueManagerId));

        Assert.Equal(h.ManagerId, h.Db.ShiftOverrides.Single().CreatedByEmployeeId);
    }

    [Fact]
    public async Task Manager_can_roster_a_plain_employee_and_themselves()
    {
        using var h = new Harness();
        var c = h.As(h.ManagerId, EmployeeRole.Manager);

        Assert.IsType<OkObjectResult>(await c.Set(h.Cover(h.SameBranchEmployeeId)));
        // Themselves too — a manager who worked the night is the person who knows it.
        Assert.IsType<OkObjectResult>(await c.Set(h.Cover(h.ManagerId)));
    }

    [Fact]
    public async Task Manager_can_roster_a_same_branch_admin()
    {
        // Allowed on purpose. The admin's ACCOUNT stays out of reach — reset-pin, role and the login
        // phone all still ask CanManageEmployeeAsync, which stops at Role==Employee.
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.As(h.ManagerId, EmployeeRole.Manager).Set(h.Cover(h.SameBranchAdminId)));
    }

    [Fact]
    public async Task Manager_cannot_roster_another_branch()
    {
        // The boundary that did NOT move.
        using var h = new Harness();

        Assert.True(Forbidden(await h.As(h.ManagerId, EmployeeRole.Manager).Set(h.Cover(h.OtherBranchEmployeeId))));
        Assert.Empty(h.Db.ShiftOverrides);
    }

    [Fact]
    public async Task An_employee_may_roster_nobody_not_even_themselves()
    {
        // Rostering is a scheduling act about other people; a worker has no part in it. Their own
        // hours are not theirs to redefine either — that is what makes it a roster and not a claim.
        using var h = new Harness();
        var c = h.As(h.SameBranchEmployeeId, EmployeeRole.Employee);

        Assert.True(Forbidden(await c.Set(h.Cover(h.ColleagueManagerId))));
        Assert.True(Forbidden(await c.Set(h.Cover(h.SameBranchEmployeeId))));
    }

    [Fact]
    public async Task Admin_rosters_anyone_in_the_company()
    {
        using var h = new Harness();
        var c = h.As(h.SameBranchAdminId, EmployeeRole.Admin);

        Assert.IsType<OkObjectResult>(await c.Set(h.Cover(h.OtherBranchEmployeeId)));
    }

    [Fact]
    public async Task Reading_and_removing_follow_the_same_boundary_as_writing()
    {
        // A gate that only guards the way in is not a gate: a manager who may add a cover day must be
        // able to take back their own mistake, and one who may not add must not be able to delete.
        using var h = new Harness();
        await h.As(h.ManagerId, EmployeeRole.Manager).Set(h.Cover(h.ColleagueManagerId));
        var id = h.Db.ShiftOverrides.Single().Id;

        Assert.IsType<OkObjectResult>(await h.As(h.ManagerId, EmployeeRole.Manager).ForEmployee(h.ColleagueManagerId));
        Assert.True(Forbidden(await h.As(h.ManagerId, EmployeeRole.Manager).ForEmployee(h.OtherBranchEmployeeId)));
        Assert.IsType<OkObjectResult>(await h.As(h.ManagerId, EmployeeRole.Manager).Remove(id));
        Assert.Empty(h.Db.ShiftOverrides);
    }
}
