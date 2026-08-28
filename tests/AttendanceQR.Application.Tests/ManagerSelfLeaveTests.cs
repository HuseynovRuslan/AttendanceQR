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
/// Who a manager may record an ABSENCE for — and the account powers that stayed shut.
///
/// The branch-only takeover fix (see ManagerAccountScopeTests) routed every manager write through
/// ManageableEmployeeAsync, which refuses any target that is not a plain Employee. That is right for
/// the two endpoints that can take an account over — the employee edit and reset-pin — and wrong for
/// filing a leave, which touches no PIN, no device and no role. Applied to leaves it produced, first,
/// a manager unable to record their own holiday (CleanFix, 2026-08-21), and then the same thing one
/// person over: a company with two managers where neither could record the other's, so the days
/// counted as unexcused absence until an admin entered them by hand.
///
/// Leaves are now scoped by BRANCH alone: anyone at the manager's own locations, whatever their role,
/// plus themselves. Platform operators stay out. If a test here fails it is one of two things — a
/// manager can no longer record an absence they are responsible for, or the account hole is back.
/// </summary>
public class ManagerSelfLeaveTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ManagerController Controller { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        public Guid PeerManagerId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"mgr-self-leave-{Guid.NewGuid()}").Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch,
                TenantId = TenantA,
                Name = "Camasirxana",
                Latitude = 40.4093,
                Longitude = 49.8671,
                RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15,
                QrVersion = 1,
                IsActive = true,
            });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = Branch, TenantId = TenantA });
            Db.Employees.Add(Person(ManagerId, "Menecer Ozu", EmployeeRole.Manager));
            Db.Employees.Add(Person(StaffId, "Filial Iscisi", EmployeeRole.Employee));
            Db.Employees.Add(Person(PeerManagerId, "Ikinci Menecer", EmployeeRole.Manager));
            Db.Employees.Add(Person(AdminId, "Sirket Admini", EmployeeRole.Admin));
            Db.SaveChanges();

            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", ManagerId.ToString()),
                new Claim("role", nameof(EmployeeRole.Manager)),
            }, "test");
            Controller = new ManagerController(Db, new StubHasher(), new StubSummary(), new AppOptions())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        private Employee Person(Guid id, string name, EmployeeRole role) => new()
        {
            Id = id,
            TenantId = TenantA,
            FullName = name,
            Role = role,
            LocationId = Branch,
            IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow,
            PasswordHash = "h",
        };

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

    private static LeaveRecordRequest Leave(Guid employeeId) => new(
        EmployeeId: employeeId,
        FromDate: new DateOnly(2026, 9, 1),
        ToDate: new DateOnly(2026, 9, 5),
        Type: LeaveType.Vacation,
        Note: "mezuniyyet");

    private static void AssertForbidden(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task Manager_can_file_leave_for_themselves()
    {
        using var h = new Harness();
        var result = await h.Controller.CreateLeave(Leave(h.ManagerId));
        Assert.IsType<OkObjectResult>(result);
        Assert.Single(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.ManagerId));
    }

    [Fact]
    public async Task Manager_can_still_file_leave_for_their_own_staff()
    {
        using var h = new Harness();
        Assert.IsType<OkObjectResult>(await h.Controller.CreateLeave(Leave(h.StaffId)));
    }

    [Fact]
    public async Task Manager_can_file_leave_for_a_peer_manager()
    {
        // The case this change exists for: two managers, one goes on holiday, and until now neither
        // could record it — the other was refused and the absentee could not file it while away.
        using var h = new Harness();
        Assert.IsType<OkObjectResult>(await h.Controller.CreateLeave(Leave(h.PeerManagerId)));
        Assert.Single(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.PeerManagerId));
    }

    [Fact]
    public async Task Manager_can_file_leave_for_an_admin_at_their_branch()
    {
        // Owner's call: in these companies the admin is not a second manager, they are someone who
        // looks at reports and also turns up to work. An absence of theirs is a fact about the branch.
        using var h = new Harness();
        Assert.IsType<OkObjectResult>(await h.Controller.CreateLeave(Leave(h.AdminId)));
        Assert.Single(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.AdminId));
    }

    [Fact]
    public async Task Own_leave_is_listed_back()
    {
        using var h = new Harness();
        await h.Controller.CreateLeave(Leave(h.ManagerId));
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.Leaves(null, null));
        var rows = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value);
        Assert.Single(rows);
    }

    [Fact]
    public async Task Manager_can_delete_their_own_leave()
    {
        using var h = new Harness();
        await h.Controller.CreateLeave(Leave(h.ManagerId));
        var id = h.Db.LeaveRecords.Single(l => l.EmployeeId == h.ManagerId).Id;
        Assert.IsType<OkObjectResult>(await h.Controller.DeleteLeave(id));
        Assert.Empty(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.ManagerId));
    }

    [Fact]
    public async Task A_peer_managers_leave_is_visible_and_deletable()
    {
        using var h = new Harness();
        h.Db.LeaveRecords.Add(new LeaveRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            EmployeeId = h.PeerManagerId,
            FromDate = new DateOnly(2026, 9, 1),
            ToDate = new DateOnly(2026, 9, 2),
            Type = LeaveType.Vacation,
            CreatedByEmployeeId = h.AdminId,
        });
        h.Db.SaveChanges();
        var peerLeaveId = h.Db.LeaveRecords.Single(l => l.EmployeeId == h.PeerManagerId).Id;

        // Filing without seeing would be a screen that swallows what it was given; seeing without
        // deleting would be a typo nobody can take back. The row records who filed it either way.
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.Leaves(null, null));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value));
        Assert.IsType<OkObjectResult>(await h.Controller.DeleteLeave(peerLeaveId));
        Assert.Empty(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.PeerManagerId));
    }

    [Fact]
    public async Task Employee_picker_offers_self_only_when_asked()
    {
        // The ACCOUNT list is unchanged by the leave widening: still plain staff, plus self on request.
        // It feeds edit and reset-pin, and it projects phone, email and birth date.
        using var h = new Harness();
        var without = Assert.IsType<OkObjectResult>(await h.Controller.Employees(false));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(without.Value));

        var with = Assert.IsType<OkObjectResult>(await h.Controller.Employees(true));
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<object>>(with.Value).Count());
    }

    [Fact]
    public async Task The_leave_picker_offers_staff_peers_and_self()
    {
        using var h = new Harness();
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.LeaveSubjectList());
        var rows = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).ToList();

        // staff + peer manager + admin + self
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public async Task The_leave_picker_carries_no_contact_details()
    {
        // Why this endpoint exists instead of a wider GET employees. A manager has no business editing
        // a peer or an admin, so handing over their telephone number and email to let a name be picked
        // from a list would be paying for the feature with somebody else's privacy.
        using var h = new Harness();
        var ok = Assert.IsType<OkObjectResult>(await h.Controller.LeaveSubjectList());
        var row = Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value).First();
        var fields = row.GetType().GetProperties().Select(pr => pr.Name).ToList();

        Assert.DoesNotContain("phoneNumber", fields);
        Assert.DoesNotContain("email", fields);
        Assert.DoesNotContain("birthDate", fields);
        Assert.Contains("fullName", fields);
    }

    [Fact]
    public async Task Another_branchs_manager_is_still_out_of_reach()
    {
        // Branch scope is what carries the whole rule now that role no longer does. If this fails,
        // widening leaves has quietly made every manager in the company reachable by every other.
        using var h = new Harness();
        var elsewhere = Guid.NewGuid();
        h.Db.Employees.Add(new Employee
        {
            Id = elsewhere, TenantId = TenantA, FullName = "Basqa Filial Meneceri",
            Role = EmployeeRole.Manager, IsActive = true, PasswordHash = "h",
            LocationId = Guid.NewGuid(), ActivatedAtUtc = DateTime.UtcNow,
        });
        h.Db.SaveChanges();

        var result = await h.Controller.CreateLeave(Leave(elsewhere));
        Assert.IsNotType<OkObjectResult>(result);
        Assert.Empty(h.Db.LeaveRecords.Where(l => l.EmployeeId == elsewhere));
    }

    [Fact]
    public async Task Widening_leaves_did_not_widen_the_account_powers()
    {
        // The point of the split. Leaves went branch-scoped; reset-pin did NOT. A manager reaching a
        // same-branch admin's PIN is the 2026-08-08 takeover, and it stays shut.
        using var h = new Harness();
        AssertForbidden(await h.Controller.ResetPin(h.AdminId));
        AssertForbidden(await h.Controller.ResetPin(h.PeerManagerId));
    }
}
