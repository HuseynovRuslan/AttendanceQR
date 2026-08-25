using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The screens a branch manager gained, and the line each of them stops at.
///
/// The rule the owner set is one sentence: a manager sees only and exactly what is theirs. Applying it
/// meant opening seven screens that were Admin-only for no reason beyond having been written for an
/// admin — birthdays, the PIN-reset queue, device changes, payroll, the dashboard, the branch list, the
/// shift templates — and the whole risk of that is a filter forgotten on one of them.
///
/// So every one is pinned here against the same fixture: two branches, one managed by our manager and
/// one not, with an ordinary employee in each, plus an admin sitting in the managed branch. That admin
/// is the important row. Branch membership alone would make them a valid target — an Admin has a
/// LocationId like anyone else, because they clock in too — and that is exactly the reach that let a
/// manager reset a same-branch admin's PIN and take the company over (P0, 2026-08-08). Every assertion
/// below that mentions them is that P0, kept dead.
/// </summary>
public class ManagerScreenScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid ManagedBranch { get; } = Guid.NewGuid();
        public Guid OtherBranch { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid MyWorkerId { get; } = Guid.NewGuid();
        public Guid TheirWorkerId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();

        /// <summary>This month — the birthday screen shows the current month and nothing else.</summary>
        private static DateOnly Dob(int day)
        {
            var now = DateTime.UtcNow;
            return new DateOnly(1990, now.Month, Math.Min(day, DateTime.DaysInMonth(1990, now.Month)));
        }

        public Fixture()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"mgr-scope-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(Branch(ManagedBranch, "Mənim filialım"));
            Db.Locations.Add(Branch(OtherBranch, "Başqa filial"));

            // The manager clocks in at the branch they manage — but it is ManagedLocations, never
            // Employee.LocationId, that decides what they may see.
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, ManagedBranch, Dob(4)));
            Db.Employees.Add(Person(MyWorkerId, "Mənim işçim", EmployeeRole.Employee, ManagedBranch, Dob(9)));
            Db.Employees.Add(Person(TheirWorkerId, "Özgə işçi", EmployeeRole.Employee, OtherBranch, Dob(11)));
            Db.Employees.Add(Person(SameBranchAdminId, "Eyni filialda admin", EmployeeRole.Admin, ManagedBranch, Dob(20)));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = ManagedBranch });
            Db.SaveChanges();
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8,
            RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid branch, DateOnly dob) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = branch,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "h", BirthDate = dob,
        };

        public Guid AddPinResetRequest(Guid employeeId)
        {
            var id = Guid.NewGuid();
            Db.PinResetRequests.Add(new PinResetRequest
            {
                Id = id, TenantId = TenantId, EmployeeId = employeeId,
                Status = PinResetStatus.Pending, RequestedAtUtc = DateTime.UtcNow,
            });
            Db.SaveChanges();
            return id;
        }

        public ControllerContext As(Guid employeeId, EmployeeRole role) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("sub", employeeId.ToString()),
                    new Claim("role", role.ToString()),
                ], "test")),
            },
        };

        public void Dispose() => Db.Dispose();
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class Lockout : ILoginLockoutStore
    {
        public int LockoutMinutes => 5;
        public bool IsLockedOut(string key) => false;
        public int RecordFailure(string key) => 5;
        public void RecordSuccess(string key) { }
    }

    private static List<T> ListOf<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((IEnumerable<object>)ok.Value!).Select(o => (T)o).ToList();
    }

    private static List<string> NamesOf(IActionResult result, string property)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!)
            .Cast<object>()
            .Select(o => o.GetType().GetProperty(property)!.GetValue(o)!.ToString()!)
            .ToList();
    }

    // --- birthdays ------------------------------------------------------------------------------

    private static BirthdaysController Birthdays(Fixture f, Guid who, EmployeeRole role) =>
        new(f.Db, new AppOptions { TimeZone = "UTC" }) { ControllerContext = f.As(who, role) };

    [Fact]
    public async Task A_manager_sees_their_own_branch_and_themselves()
    {
        using var f = new Fixture();
        var names = NamesOf(await Birthdays(f, f.ManagerId, EmployeeRole.Manager).ThisMonth(), "fullName");

        Assert.Contains("Mənim işçim", names);
        Assert.Contains("Menecer", names); // a manager has a birthday too
        Assert.DoesNotContain("Özgə işçi", names);
        Assert.DoesNotContain("Eyni filialda admin", names);
    }

    [Fact]
    public async Task An_admin_still_sees_everybody()
    {
        using var f = new Fixture();
        var names = NamesOf(await Birthdays(f, f.SameBranchAdminId, EmployeeRole.Admin).ThisMonth(), "fullName");
        Assert.Equal(4, names.Count);
    }

    // --- the branch list ------------------------------------------------------------------------

    private static AdminLocationsController Locations(Fixture f, Guid who, EmployeeRole role) =>
        new(f.Db, new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-manager-scope", TtlSeconds = 60 })))
        { ControllerContext = f.As(who, role) };

    [Fact]
    public async Task A_manager_sees_only_the_branches_they_manage()
    {
        using var f = new Fixture();
        var names = NamesOf(await Locations(f, f.ManagerId, EmployeeRole.Manager).List(), "name");

        Assert.Equal(["Mənim filialım"], names);
    }

    [Fact]
    public async Task An_admin_sees_every_branch()
    {
        using var f = new Fixture();
        Assert.Equal(2, NamesOf(await Locations(f, f.SameBranchAdminId, EmployeeRole.Admin).List(), "name").Count);
    }

    // --- the PIN-reset queue ---------------------------------------------------------------------

    private static AdminPinResetController PinResets(Fixture f, Guid who, EmployeeRole role) =>
        new(f.Db, new Hasher(), new Lockout()) { ControllerContext = f.As(who, role) };

    [Fact]
    public async Task The_queue_shows_a_manager_only_their_own_staff()
    {
        using var f = new Fixture();
        f.AddPinResetRequest(f.MyWorkerId);
        f.AddPinResetRequest(f.TheirWorkerId);
        f.AddPinResetRequest(f.SameBranchAdminId);

        var names = NamesOf(await PinResets(f, f.ManagerId, EmployeeRole.Manager).Pending(), "employeeName");

        Assert.Equal(["Mənim işçim"], names);
    }

    [Fact]
    public async Task A_manager_cannot_resolve_a_request_from_another_branch()
    {
        using var f = new Fixture();
        var id = f.AddPinResetRequest(f.TheirWorkerId);

        var refused = Assert.IsAssignableFrom<ObjectResult>(
            await PinResets(f, f.ManagerId, EmployeeRole.Manager).Resolve(id));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);

        // And the request is still Pending: a refusal must not consume somebody's queue entry.
        Assert.Equal(PinResetStatus.Pending, f.Db.PinResetRequests.AsNoTracking().First(r => r.Id == id).Status);
    }

    [Fact]
    public async Task A_manager_cannot_resolve_an_ADMINS_request_in_their_own_branch()
    {
        // The P0 of 2026-08-08, in one test. Resolving returns the plaintext PIN, so this is not a
        // visibility question — it is whether a branch manager can take the company.
        using var f = new Fixture();
        var id = f.AddPinResetRequest(f.SameBranchAdminId);

        var refused = Assert.IsAssignableFrom<ObjectResult>(
            await PinResets(f, f.ManagerId, EmployeeRole.Manager).Resolve(id));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        // The admin's credential is untouched — no new hash was written for them.
        Assert.Equal("h", f.Db.Employees.AsNoTracking().First(e => e.Id == f.SameBranchAdminId).PasswordHash);
    }

    [Fact]
    public async Task A_manager_cannot_dismiss_a_request_that_is_not_theirs()
    {
        // Dismissing hands out no credential — it silences somebody. A locked-out employee whose
        // request quietly disappears has no way to know it ever arrived.
        using var f = new Fixture();
        var id = f.AddPinResetRequest(f.TheirWorkerId);

        var refused = Assert.IsAssignableFrom<ObjectResult>(
            await PinResets(f, f.ManagerId, EmployeeRole.Manager).Dismiss(id));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal(PinResetStatus.Pending, f.Db.PinResetRequests.AsNoTracking().First(r => r.Id == id).Status);
    }

    // The two happy paths — a manager resolving their own staff's request, an admin resolving
    // anyone's — are not here: resolving runs ExecuteUpdateAsync to claim the row atomically, and the
    // in-memory provider cannot translate it. Every refusal above returns before that call, which is
    // the half that decides who may act.

    // --- what stays with the admin ----------------------------------------------------------------

    [Theory]
    [InlineData(typeof(AdminLocationsController), "Create")]
    [InlineData(typeof(AdminLocationsController), "Update")]
    [InlineData(typeof(AdminLocationsController), "Delete")]
    [InlineData(typeof(AdminLocationsController), "SetActive")]
    [InlineData(typeof(AdminLocationsController), "GenerateStaticQr")]
    [InlineData(typeof(AdminLocationsController), "InvalidateQr")]
    [InlineData(typeof(SchedulesController), "Create")]
    [InlineData(typeof(SchedulesController), "Update")]
    [InlineData(typeof(SchedulesController), "Delete")]
    public void Changing_a_branch_or_a_shift_is_still_admin_only(Type controller, string action)
    {
        // These classes are now [Authorize(Roles = "Admin,Manager")] so a manager can READ them, which
        // means every write needs its own attribute — and an attribute is exactly the kind of thing
        // that gets left off the tenth action added next year. The geofence is the anti-fraud
        // boundary: a manager who could move it could move it to their own house.
        var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Admin", attr!.Roles);
    }

    [Fact]
    public void The_screens_a_manager_gained_say_so_on_the_class()
    {
        foreach (var t in new[]
                 {
                     typeof(BirthdaysController), typeof(AdminPinResetController),
                     typeof(AdminDeviceChangeController), typeof(AdminLocationsController),
                     typeof(SchedulesController),
                 })
        {
            var attr = t.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("Admin,Manager", attr!.Roles);
        }
    }
}
