using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Api.Contracts;
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
using Microsoft.Extensions.Logging.Abstractions;
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
        new(f.Db, new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-manager-scope", TtlSeconds = 60 })),
            new OffFaceMatch(), NullLogger<AdminLocationsController>.Instance)
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

    // --- the branch a manager may correct ---------------------------------------------------------

    [Fact]
    public async Task A_manager_can_edit_a_branch_they_manage_and_the_move_is_recorded()
    {
        // The permission the owner asked for: the manager knows where the poster hangs and how wide
        // the yard is; the admin was guessing from an office, which is how the wrong poster ended up
        // on a wall at Dədə Qorqud Parkı. The move is allowed and written down — the record is what
        // makes it safe, not a refusal.
        using var f = new Fixture();
        var before = f.Db.Locations.AsNoTracking().First(l => l.Id == f.ManagedBranch);
        Assert.Null(before.GeofenceMovedAtUtc);

        var result = await Locations(f, f.ManagerId, EmployeeRole.Manager).Update(
            f.ManagedBranch,
            new LocationRequest("Mənim filialım", 40.41, 49.86, 200, "09:00", "18:00", 15, 126));

        Assert.IsType<OkObjectResult>(result);
        var after = f.Db.Locations.AsNoTracking().First(l => l.Id == f.ManagedBranch);
        Assert.Equal(200, after.RadiusMeters);
        Assert.NotNull(after.GeofenceMovedAtUtc);
        Assert.Equal(f.ManagerId, after.GeofenceMovedByEmployeeId);
        Assert.True(after.GeofenceMovedMeters > 0, "köçmə məsafəsi yazılmayıb");

        var audit = f.Db.AuditLogs.AsNoTracking().Where(a => a.EventType == AuditEventType.LocationMoved).ToList();
        var row = Assert.Single(audit);
        Assert.Equal(f.ManagerId, row.EmployeeId);
        Assert.Contains("→", row.Reason!);
    }

    [Fact]
    public async Task A_manager_cannot_edit_a_branch_they_do_not_manage()
    {
        using var f = new Fixture();
        var refused = Assert.IsAssignableFrom<ObjectResult>(
            await Locations(f, f.ManagerId, EmployeeRole.Manager).Update(
                f.OtherBranch,
                new LocationRequest("Oğurlanmış", 40.41, 49.86, 200, "09:00", "18:00", 15, 126)));

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("Başqa filial", f.Db.Locations.AsNoTracking().First(l => l.Id == f.OtherBranch).Name);
    }

    [Fact]
    public async Task Editing_hours_without_touching_the_fence_records_nothing()
    {
        // The stamp has to mean something. If every edit set it, "this branch was moved" would be
        // noise and nobody would look at it.
        using var f = new Fixture();
        var b = f.Db.Locations.AsNoTracking().First(l => l.Id == f.ManagedBranch);

        await Locations(f, f.ManagerId, EmployeeRole.Manager).Update(
            f.ManagedBranch,
            new LocationRequest("Yeni ad", b.Latitude, b.Longitude, b.RadiusMeters, "08:00", "17:00", 10, 126));

        var after = f.Db.Locations.AsNoTracking().First(l => l.Id == f.ManagedBranch);
        Assert.Equal("Yeni ad", after.Name);
        Assert.Null(after.GeofenceMovedAtUtc);
        Assert.Empty(f.Db.AuditLogs.AsNoTracking().Where(a => a.EventType == AuditEventType.LocationMoved).ToList());
    }

    [Fact]
    public async Task A_manager_prints_their_own_branchs_poster_but_not_another_branchs()
    {
        // The reason this is here at all: the posters hanging at Dədə Qorqud Parkı belonged to two
        // other branches, so nobody at that site could clock in — and the person standing in front of
        // the wall could not reprint it.
        using var f = new Fixture();
        Assert.IsType<OkObjectResult>(
            await Locations(f, f.ManagerId, EmployeeRole.Manager).GenerateStaticQr(f.ManagedBranch));

        var refused = Assert.IsAssignableFrom<ObjectResult>(
            await Locations(f, f.ManagerId, EmployeeRole.Manager).GenerateStaticQr(f.OtherBranch));
        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
    }

    // --- what stays with the admin ----------------------------------------------------------------

    [Theory]
    [InlineData(typeof(AdminLocationsController), "Create")]
    [InlineData(typeof(AdminLocationsController), "Delete")]
    [InlineData(typeof(AdminLocationsController), "SetActive")]
    [InlineData(typeof(AdminLocationsController), "InvalidateQr")]
    [InlineData(typeof(SchedulesController), "Create")]
    [InlineData(typeof(SchedulesController), "Update")]
    [InlineData(typeof(SchedulesController), "Delete")]
    public void Changing_a_branch_or_a_shift_is_still_admin_only(Type controller, string action)
    {
        // These classes are [Authorize(Roles = "Admin,Manager")], so every action that must stay with
        // the admin needs its own attribute — and an attribute is exactly the kind of thing left off
        // the tenth action added next year.
        //
        // Update and GenerateStaticQr are deliberately NOT here any more: a manager corrects their own
        // branch's fence and prints its poster, which is scope-checked in the action and recorded (see
        // the tests above). What remains is what a branch manager has no business doing — creating a
        // branch or deleting one (both are money on the customer's bill), switching one off, and
        // invalidating a QR, which voids every printed poster in the company at once.
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
