using System.Security.Claims;
using AttendanceQR.Api.Controllers;
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
/// Bound devices, now that a branch manager can work on them.
///
/// "My phone will not scan" is a complaint the manager standing next to the poster gets first, and
/// until this widening they could do nothing about it but telephone an admin. So the device list and
/// the revoke button moved down to them — narrowed to the branches they oversee.
///
/// Detaching a device is not a small button. A revoked binding is never re-adopted by the next scan
/// (that is the whole point of RevokedAtUtc), so a wrong revoke means somebody cannot clock in
/// tomorrow morning, and the record they cannot make is their pay. Everything below is about who a
/// manager may point that button at.
///
/// Two boundaries in particular. A manager may SEE anyone at their branch — including an admin who
/// clocks in there — but may only ACT on Role==Employee: that split is the 2026-08-08 P0, where
/// branch membership alone let a manager reach a same-branch admin's account and take the company.
/// Every new manager surface has to arrive already behind it, and the device list is a fresh route to
/// exactly that account. The other is the shared brigade handset, which can carry people from two
/// branches at once: revoke-all detaches the caller's own people and reports the rest as skipped,
/// because silently detaching a worker a different manager answers for strands somebody nobody warned.
/// </summary>
public class DeviceBindingManagerScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000db");

    /// <summary>The handset a brigade shares — deliberately straddles both branches.</summary>
    private const string BrigadePhone = "briqada-telefonu";

    /// <summary>A phone belonging to one person at the branch the manager does NOT oversee.</summary>
    private const string OtherBranchPhone = "yad-filial-telefonu";

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; }

        public Guid MyBranch { get; } = Guid.NewGuid();      // the manager's ManagedLocations set
        public Guid OtherBranch { get; } = Guid.NewGuid();   // same company, somebody else's branch

        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        // An admin who clocks in at the manager's own branch — carries MyBranch as their LocationId,
        // which is precisely why branch membership alone was never a sufficient rule.
        public Guid BranchAdminId { get; } = Guid.NewGuid();
        public Guid MyWorkerId { get; } = Guid.NewGuid();
        public Guid MySecondWorkerId { get; } = Guid.NewGuid();
        public Guid OtherWorkerId { get; } = Guid.NewGuid();

        public Guid SoloBindingId { get; }
        public Guid BranchAdminBindingId { get; }
        public Guid OtherBranchBindingId { get; }
        public Guid BrigadeMineId { get; }
        public Guid BrigadeSecondId { get; }
        public Guid BrigadeOtherBranchId { get; }

        public Fixture()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"device-binding-scope-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true,
            });
            Db.Locations.Add(Branch(MyBranch, "Merkez"));
            Db.Locations.Add(Branch(OtherBranch, "Novxani"));
            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = MyBranch, TenantId = TenantId,
            });

            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, MyBranch));
            Db.Employees.Add(Person(AdminId, "Sirket Admini", EmployeeRole.Admin, MyBranch));
            Db.Employees.Add(Person(BranchAdminId, "Filial Admini", EmployeeRole.Admin, MyBranch));
            Db.Employees.Add(Person(MyWorkerId, "Oz Iscim", EmployeeRole.Employee, MyBranch));
            Db.Employees.Add(Person(MySecondWorkerId, "Ikinci Iscim", EmployeeRole.Employee, MyBranch));
            Db.Employees.Add(Person(OtherWorkerId, "Yad Filial Iscisi", EmployeeRole.Employee, OtherBranch));

            SoloBindingId = Bind(MyWorkerId, "oz-telefonu", "Redmi 9");
            BranchAdminBindingId = Bind(BranchAdminId, "admin-telefonu", "iPhone 13");
            OtherBranchBindingId = Bind(OtherWorkerId, OtherBranchPhone, "Samsung A12");
            BrigadeMineId = Bind(MyWorkerId, BrigadePhone, "Nokia G21");
            BrigadeSecondId = Bind(MySecondWorkerId, BrigadePhone, "Nokia G21");
            BrigadeOtherBranchId = Bind(OtherWorkerId, BrigadePhone, "Nokia G21");

            Db.SaveChanges();
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8,
            RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid branch) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = branch,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "h",
            CanShareDevice = true,
        };

        private Guid Bind(Guid employeeId, string fingerprint, string label)
        {
            var binding = new DeviceBinding
            {
                TenantId = TenantId, EmployeeId = employeeId, DeviceFingerprint = fingerprint,
                DeviceLabel = label, BoundVia = DeviceBindingOrigin.Activation, IsActive = true,
            };
            Db.DeviceBindings.Add(binding);
            return binding.Id;
        }

        public AdminDeviceBindingController As(Guid who, EmployeeRole role) => new(Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", who.ToString()),
                        new Claim("role", role.ToString()),
                    ], "test")),
                },
            },
        };

        public AdminDeviceBindingController Manager => As(ManagerId, EmployeeRole.Manager);

        public AdminDeviceBindingController Admin => As(AdminId, EmployeeRole.Admin);

        public DeviceBinding Binding(Guid id)
            => Db.DeviceBindings.AsNoTracking().Single(d => d.Id == id);

        /// <summary>Still usable at the gate: not revoked, so the next scan is still accepted.</summary>
        public bool StillBound(Guid id)
        {
            var b = Binding(id);
            return b.RevokedAtUtc is null && b.IsActive;
        }

        public void Dispose() => Db.Dispose();
    }

    // --- reading the results -----------------------------------------------------------------------

    private static List<object> Rows(IActionResult result)
        => ((System.Collections.IEnumerable)Assert.IsType<OkObjectResult>(result).Value!)
            .Cast<object>().ToList();

    private static T Prop<T>(object row, string name)
        => (T)row.GetType().GetProperty(name)!.GetValue(row)!;

    private static List<Guid> BindingIds(IActionResult result)
        => Rows(result).Select(r => Prop<Guid>(r, "id")).ToList();

    private static List<Guid> AccountsOn(object device)
        => ((System.Collections.IEnumerable)device.GetType().GetProperty("employees")!.GetValue(device)!)
            .Cast<object>().Select(p => Prop<Guid>(p, "employeeId")).ToList();

    private static (int Revoked, int Skipped) Counts(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (Prop<int>(ok.Value!, "revoked"), Prop<int>(ok.Value!, "skipped"));
    }

    private static void AssertForbidden(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    // --- the list ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_manager_lists_only_their_own_branches_devices()
    {
        // The list is where a revoke starts, so an unfiltered one hands a manager a button pointed at
        // a branch they do not answer for — and, on the way, a roster of who works there on which
        // handset.
        using var f = new Fixture();

        var ids = BindingIds(await f.Manager.List());

        Assert.Contains(f.SoloBindingId, ids);
        Assert.Contains(f.BrigadeMineId, ids);
        Assert.DoesNotContain(f.OtherBranchBindingId, ids);
        Assert.DoesNotContain(f.BrigadeOtherBranchId, ids);
    }

    [Fact]
    public async Task A_manager_still_sees_the_device_of_an_admin_who_clocks_in_at_their_branch()
    {
        // Seeing is deliberately wider than acting: that admin stands at the same gate every morning,
        // and a branch list missing them is a wrong list rather than a safeguard. What they may not do
        // with the row is the next test.
        using var f = new Fixture();

        Assert.Contains(f.BranchAdminBindingId, BindingIds(await f.Manager.List()));
    }

    [Fact]
    public async Task An_admin_still_lists_the_whole_company()
    {
        // The other half of the widening: narrowing the screen for managers must not have narrowed it
        // for the person who had all of it already.
        using var f = new Fixture();

        var ids = BindingIds(await f.Admin.List());

        Assert.Equal(6, ids.Count);
        Assert.Contains(f.OtherBranchBindingId, ids);
        Assert.Contains(f.BrigadeOtherBranchId, ids);
    }

    // --- revoking one device -----------------------------------------------------------------------

    [Fact]
    public async Task A_manager_may_revoke_their_own_branch_workers_device()
    {
        // If this fails the widening bought nothing and the branch is back to telephoning an admin.
        using var f = new Fixture();

        var ok = Assert.IsType<OkObjectResult>(await f.Manager.Revoke(f.SoloBindingId));

        Assert.Equal("Revoked", Prop<string>(ok.Value!, "status"));
        var row = f.Binding(f.SoloBindingId);
        Assert.False(row.IsActive);
        Assert.NotNull(row.RevokedAtUtc);   // what stops the next scan from silently re-adopting it
    }

    [Fact]
    public async Task A_manager_cannot_revoke_another_branchs_device_and_it_stays_usable()
    {
        // The consequence of getting this wrong is not an error message: it is a worker at another
        // site arriving tomorrow to a phone that will not scan, with nobody there who knows why.
        using var f = new Fixture();

        AssertForbidden(await f.Manager.Revoke(f.OtherBranchBindingId));

        Assert.True(f.StillBound(f.OtherBranchBindingId));
    }

    [Fact]
    public async Task A_manager_cannot_revoke_a_same_branch_admins_device()
    {
        // The 2026-08-08 takeover boundary, arriving with the new surface. A manager must not reach an
        // admin's account by ANY route, and cutting the admin off the poster is reaching it: the
        // account they cannot edit becomes one they can stop from working.
        using var f = new Fixture();

        AssertForbidden(await f.Manager.Revoke(f.BranchAdminBindingId));

        Assert.True(f.StillBound(f.BranchAdminBindingId));
    }

    // --- the shared handset ------------------------------------------------------------------------

    [Fact]
    public async Task The_shared_list_shows_a_manager_only_the_accounts_they_can_act_on()
    {
        // A count they can act on is the honest count for them — smaller than the handset really
        // carries, and never naming somebody at another branch.
        using var f = new Fixture();

        var device = Assert.Single(Rows(await f.Manager.Shared()));

        Assert.Equal(BrigadePhone, Prop<string>(device, "fingerprint"));
        Assert.Equal(2, Prop<int>(device, "accountCount"));
        var people = AccountsOn(device);
        Assert.Contains(f.MyWorkerId, people);
        Assert.Contains(f.MySecondWorkerId, people);
        Assert.DoesNotContain(f.OtherWorkerId, people);
    }

    [Fact]
    public async Task The_shared_list_shows_an_admin_everyone_on_the_handset()
    {
        // Somebody has to be able to see the whole phone. If nobody can, a handset quietly carrying
        // two branches is invisible to the company that owns the risk.
        using var f = new Fixture();

        var device = Assert.Single(Rows(await f.Admin.Shared()));

        Assert.Equal(3, Prop<int>(device, "accountCount"));
        Assert.Contains(f.OtherWorkerId, AccountsOn(device));
    }

    [Fact]
    public async Task Revoke_all_on_a_straddling_handset_detaches_only_the_managers_own_people()
    {
        // The lost brigade phone. The manager empties their own people off it and the other branch's
        // worker stays bound: detaching them silently would strand somebody a different manager
        // answers for, over a phone that manager was never told about.
        using var f = new Fixture();

        var counts = Counts(await f.Manager.RevokeDevice(BrigadePhone));

        Assert.Equal(2, counts.Revoked);
        Assert.Equal(1, counts.Skipped);    // so the screen can say the handset still carries somebody
        Assert.NotNull(f.Binding(f.BrigadeMineId).RevokedAtUtc);
        Assert.NotNull(f.Binding(f.BrigadeSecondId).RevokedAtUtc);
        Assert.True(f.StillBound(f.BrigadeOtherBranchId));
    }

    [Fact]
    public async Task Revoke_all_by_an_admin_empties_the_whole_handset()
    {
        // Whoever the phone straddles, the company has one person who can finish the job — otherwise a
        // stolen handset stays half-bound until two managers happen to both act on it.
        using var f = new Fixture();

        var counts = Counts(await f.Admin.RevokeDevice(BrigadePhone));

        Assert.Equal(3, counts.Revoked);
        Assert.Equal(0, counts.Skipped);
        Assert.NotNull(f.Binding(f.BrigadeOtherBranchId).RevokedAtUtc);
    }

    [Fact]
    public async Task Revoke_all_on_a_handset_carrying_nobody_of_theirs_is_refused()
    {
        // Revoke-all is addressed by fingerprint rather than by a row from the caller's own list, so
        // scope has to be re-decided here and cannot be inferred from how the screen was reached.
        using var f = new Fixture();

        AssertForbidden(await f.Manager.RevokeDevice(OtherBranchPhone));

        Assert.True(f.StillBound(f.OtherBranchBindingId));
    }

    [Fact]
    public async Task Only_the_detached_accounts_are_written_to_the_audit_log()
    {
        // The audit row is the answer to "who took my phone off the system". Logging the skipped
        // account would record a revocation that never happened, and the worker still scanning on that
        // handset would contradict the log the day somebody reads it.
        using var f = new Fixture();

        await f.Manager.RevokeDevice(BrigadePhone);

        var audited = f.Db.AuditLogs.AsNoTracking()
            .Where(a => a.EventType == AuditEventType.DeviceBindingRevoked)
            .ToList()
            .Select(a => a.EmployeeId ?? Guid.Empty)
            .ToList();

        Assert.Equal(2, audited.Count);
        Assert.Contains(f.MyWorkerId, audited);
        Assert.Contains(f.MySecondWorkerId, audited);
        Assert.DoesNotContain(f.OtherWorkerId, audited);
    }
}
