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
/// A manager may file their OWN leave — and still nobody else's above Role==Employee.
///
/// The branch-only takeover fix (see ManagerAccountScopeTests) routed every manager write through
/// ManageableEmployeeAsync, which refuses any target that is not a plain Employee, self included. It
/// caught the harmless case too: a manager's own holiday needed an admin, and until one entered it the
/// day counted as Qayıb against them (reported from CleanFix, 2026-08-21). LeaveSubjectAsync carves out
/// exactly self, for leaves only. If a test here fails, either that hole is back (a peer's or an
/// admin's record) or a manager can no longer record their own absence.
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
    public async Task Manager_cannot_file_leave_for_a_peer_manager()
    {
        using var h = new Harness();
        AssertForbidden(await h.Controller.CreateLeave(Leave(h.PeerManagerId)));
        Assert.Empty(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.PeerManagerId));
    }

    [Fact]
    public async Task Manager_cannot_file_leave_for_an_admin()
    {
        using var h = new Harness();
        AssertForbidden(await h.Controller.CreateLeave(Leave(h.AdminId)));
        Assert.Empty(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.AdminId));
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
    public async Task A_peer_managers_leave_stays_invisible_and_undeletable()
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

        var ok = Assert.IsType<OkObjectResult>(await h.Controller.Leaves(null, null));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<object>>(ok.Value));
        AssertForbidden(await h.Controller.DeleteLeave(peerLeaveId));
        Assert.Single(h.Db.LeaveRecords.Where(l => l.EmployeeId == h.PeerManagerId));
    }

    [Fact]
    public async Task Employee_picker_offers_self_only_when_asked()
    {
        using var h = new Harness();
        var without = Assert.IsType<OkObjectResult>(await h.Controller.Employees(false));
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(without.Value));

        var with = Assert.IsType<OkObjectResult>(await h.Controller.Employees(true));
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<object>>(with.Value).Count());
    }
}
