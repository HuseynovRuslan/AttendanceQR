using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
/// Granting an opt-in capability to a whole branch at once — and the boundary that did not move.
///
/// Both capabilities are things a MANAGER knows and an admin does not: which of the brigade owns no
/// phone, which site has no QR poster. Making them ask for every name is how a permission ends up
/// either never granted or granted to everyone, so the owner asked for the bulk action to reach them.
///
/// That is a real widening and the tests exist for its edges. A manager may grant across their own
/// branches' plain staff and no further: not to a fellow manager, not to an admin who clocks in at
/// their site, not to another branch. That is the 2026-08-08 rule — the one that stopped a manager
/// reaching a same-branch admin's account — and handing out capabilities must not become a way past
/// it. Names out of reach are skipped rather than failing the call, because a bulk action over a
/// filtered list will sometimes include one and refusing everything would teach managers to stop
/// using the button.
/// </summary>
public class BulkPermissionScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e7");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ManagerController Manager { get; }
        public Guid MyBranch { get; } = Guid.NewGuid();
        public Guid OtherBranch { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        public Guid PeerManagerId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid OtherBranchStaffId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"bulk-perm-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true,
            });
            foreach (var (id, name) in new[] { (MyBranch, "Merkez"), (OtherBranch, "Novxani") })
                Db.Locations.Add(new Location
                {
                    Id = id, TenantId = TenantId, Name = name,
                    Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                    LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
                });

            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = MyBranch, TenantId = TenantId,
            });

            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, MyBranch));
            Db.Employees.Add(Person(StaffId, "Oz Iscim", EmployeeRole.Employee, MyBranch));
            Db.Employees.Add(Person(PeerManagerId, "Ikinci Menecer", EmployeeRole.Manager, MyBranch));
            Db.Employees.Add(Person(AdminId, "Sirket Admini", EmployeeRole.Admin, MyBranch));
            Db.Employees.Add(Person(OtherBranchStaffId, "Basqa Filial Iscisi", EmployeeRole.Employee, OtherBranch));
            Db.SaveChanges();

            Manager = new ManagerController(Db, new StubHasher(), new StubSummary(), new AppOptions())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", ManagerId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Manager)),
                        }, "test")),
                    },
                },
            };
        }

        private Employee Person(Guid id, string name, EmployeeRole role, Guid location) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true,
            PasswordHash = "h", LocationId = location, ActivatedAtUtc = DateTime.UtcNow,
        };

        public bool Can(Guid id, BulkPermission which)
        {
            var e = Db.Employees.Single(x => x.Id == id);
            return which == BulkPermission.ShareDevice ? e.CanShareDevice : e.CanFieldCheckIn;
        }

        public Task<IActionResult> GrantAsync(IEnumerable<Guid> ids, BulkPermission which, bool allowed)
            => Manager.BulkGrant(new BulkPermissionRequest(ids.ToList(), which, allowed));

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

    [Theory]
    [InlineData(BulkPermission.ShareDevice)]
    [InlineData(BulkPermission.FieldCheckIn)]
    public async Task A_manager_may_grant_across_their_own_staff(BulkPermission which)
    {
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.GrantAsync([h.StaffId], which, true));
        Assert.True(h.Can(h.StaffId, which));
    }

    [Theory]
    [InlineData(BulkPermission.ShareDevice)]
    [InlineData(BulkPermission.FieldCheckIn)]
    public async Task And_may_take_it_back(BulkPermission which)
    {
        // A permission that can only be switched on is a ratchet — the day a brigade stops sharing a
        // phone, the way back has to be no harder than the way in.
        using var h = new Harness();

        await h.GrantAsync([h.StaffId], which, true);
        await h.GrantAsync([h.StaffId], which, false);

        Assert.False(h.Can(h.StaffId, which));
    }

    [Fact]
    public async Task A_manager_cannot_reach_a_peer_or_an_admin()
    {
        // The 2026-08-08 boundary. Capabilities must not become the way around it: a manager who could
        // grant one to a same-branch admin is a manager acting on an account above their own.
        using var h = new Harness();

        await h.GrantAsync([h.PeerManagerId, h.AdminId], BulkPermission.ShareDevice, true);

        Assert.False(h.Can(h.PeerManagerId, BulkPermission.ShareDevice));
        Assert.False(h.Can(h.AdminId, BulkPermission.ShareDevice));
    }

    [Fact]
    public async Task A_manager_cannot_reach_another_branch()
    {
        using var h = new Harness();

        await h.GrantAsync([h.OtherBranchStaffId], BulkPermission.FieldCheckIn, true);

        Assert.False(h.Can(h.OtherBranchStaffId, BulkPermission.FieldCheckIn));
    }

    [Fact]
    public async Task Out_of_reach_names_are_skipped_not_fatal()
    {
        // A filtered list will sometimes carry one. Refusing the whole call would leave the manager
        // with a button that fails for reasons they cannot see, and they would stop using it.
        using var h = new Harness();

        var result = await h.GrantAsync(
            [h.StaffId, h.AdminId, h.OtherBranchStaffId], BulkPermission.ShareDevice, true);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, ok.Value!.GetType().GetProperty("changed")!.GetValue(ok.Value));
        Assert.Equal(2, ok.Value.GetType().GetProperty("skipped")!.GetValue(ok.Value));
        Assert.True(h.Can(h.StaffId, BulkPermission.ShareDevice));
    }

    [Fact]
    public async Task The_managers_own_list_reports_the_permission_back()
    {
        // It was projected and then dropped from the reshaped response, so the manager's screen
        // counted zero however many people had it and "take it back" computed an empty set and
        // returned in silence. A grant you cannot see is a grant nobody trusts.
        using var h = new Harness();
        await h.GrantAsync([h.StaffId], BulkPermission.ShareDevice, true);

        var ok = Assert.IsType<OkObjectResult>(await h.Manager.Employees(false));
        var row = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value)
            .Single(r => (Guid)r.GetType().GetProperty("id")!.GetValue(r)! == h.StaffId);

        var field = row.GetType().GetProperty("canShareDevice");
        Assert.NotNull(field);
        Assert.Equal(true, field!.GetValue(row));
    }

    [Fact]
    public async Task An_empty_list_is_refused_rather_than_silently_doing_nothing()
    {
        using var h = new Harness();
        Assert.IsType<BadRequestObjectResult>(await h.GrantAsync([], BulkPermission.ShareDevice, true));
    }

    [Fact]
    public void The_two_capabilities_do_not_bleed_into_each_other()
    {
        // One helper sets both, so the obvious way to get this wrong is to write the wrong field.
        var e = new Employee { FullName = "x", PasswordHash = "h" };

        AdminController.ApplyPermission([e], BulkPermission.ShareDevice, true);
        Assert.True(e.CanShareDevice);
        Assert.False(e.CanFieldCheckIn);

        AdminController.ApplyPermission([e], BulkPermission.FieldCheckIn, true);
        Assert.True(e.CanFieldCheckIn);
        Assert.True(e.CanShareDevice);
    }

    [Fact]
    public void Rows_already_in_the_wanted_state_are_not_counted_as_changed()
    {
        // The number the screen reports back. Counting no-ops would tell an admin they had just
        // granted something to forty people when they granted it to two.
        var already = new Employee { FullName = "x", PasswordHash = "h", CanShareDevice = true };
        var not = new Employee { FullName = "y", PasswordHash = "h" };

        Assert.Equal(1, AdminController.ApplyPermission([already, not], BulkPermission.ShareDevice, true));
    }
}
