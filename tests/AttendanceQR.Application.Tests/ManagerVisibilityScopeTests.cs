using System.Collections;
using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Reporting;
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
/// Pins the manager VISIBILITY boundary — the read-side completion of the P0 in
/// <see cref="ManagerAccountScopeTests"/>. A manager's panel (drill-downs via CanAccessEmployeeAsync,
/// report summaries via LocationScope, missed-checkout review) shows only Role==Employee workers in
/// their managed branches, plus themselves; a same-branch admin's records, selfies and summaries are
/// no longer theirs to read, and a missed-checkout review can never touch an admin's day — or
/// self-approve the manager's own.
/// </summary>
public class ManagerVisibilityScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid BranchA { get; } = Guid.NewGuid();   // managed
        public Guid BranchB { get; } = Guid.NewGuid();   // same tenant, not managed
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid OtherBranchEmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"mgr-vis-{Guid.NewGuid()}")
                .Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Locations.Add(Branch(BranchA, "Filial A"));
            Db.Locations.Add(Branch(BranchB, "Filial B"));
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantA });
            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, BranchA));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Filial İşçisi", EmployeeRole.Employee, BranchA));
            Db.Employees.Add(Person(SameBranchAdminId, "Filial Admini", EmployeeRole.Admin, BranchA));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Başqa Filial", EmployeeRole.Employee, BranchB));
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

        public Task<bool> Access(Guid requester, EmployeeRole role, Guid target) =>
            LocationScopeRules.CanAccessEmployeeAsync(Db, requester, role, target, CancellationToken.None);

        public DailySummary Summary(Guid employeeId, Guid locationId)
        {
            var s = new DailySummary
            {
                EmployeeId = employeeId, LocationId = locationId, TenantId = TenantA,
                SummaryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            };
            Db.DailySummaries.Add(s);
            Db.SaveChanges();
            return s;
        }

        public AdminMissedCheckoutController MissedCheckout(Guid callerId, EmployeeRole role)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", callerId.ToString()),
                new Claim("role", role.ToString()),
            }, "test");
            return new AdminMissedCheckoutController(Db, new StubSummaryService())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        public MissedCheckoutRequest Request(Guid employeeId)
        {
            var record = new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantA, EmployeeId = employeeId, LocationId = BranchA,
                AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
                CheckInAtUtc = DateTime.UtcNow.AddHours(-10),
            };
            Db.AttendanceRecords.Add(record);
            var mc = new MissedCheckoutRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantA, EmployeeId = employeeId,
                AttendanceRecordId = record.Id, AttendanceDate = record.AttendanceDate,
                RequestedCheckOutAtUtc = DateTime.UtcNow.AddHours(-1), Reason = "unutdum",
                RequestedAtUtc = DateTime.UtcNow,
            };
            Db.MissedCheckoutRequests.Add(mc);
            Db.SaveChanges();
            return mc;
        }

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubSummaryService : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    // --- CanAccessEmployeeAsync: the drill-down rule (records, selfies) ----------

    [Fact]
    public async Task Manager_can_access_same_branch_employee()
    {
        using var h = new Harness();
        Assert.True(await h.Access(h.ManagerId, EmployeeRole.Manager, h.SameBranchEmployeeId));
    }

    [Fact]
    public async Task Manager_CAN_see_a_same_branch_admin_but_still_cannot_manage_them()
    {
        // The two halves of what used to be one rule, and the whole reason they were split.
        //
        // Seeing was collateral damage: a site with two managers reported a headcount short by one,
        // with nothing on screen to explain the gap — 97 people read as 95 and the owner had to ask
        // why. They work the same site and see each other every morning; hiding the row was not
        // protecting anything.
        //
        // Managing is the P0 of 2026-08-08 and does not move. A manager who could act on a
        // same-branch ADMIN could reset that admin's PIN, read the plaintext back, and take the
        // company. Both assertions belong in one test so nobody widens the second while widening the
        // first.
        using var h = new Harness();

        Assert.True(await h.Access(h.ManagerId, EmployeeRole.Manager, h.SameBranchAdminId));
        Assert.False(await LocationScopeRules.CanManageEmployeeAsync(
            h.Db, h.ManagerId, EmployeeRole.Manager, h.SameBranchAdminId, CancellationToken.None));
    }

    [Fact]
    public async Task Seeing_another_branch_is_still_refused()
    {
        // Widening "who can I see" to the whole branch must not widen it to the whole company.
        using var h = new Harness();
        Assert.False(await h.Access(h.ManagerId, EmployeeRole.Manager, h.OtherBranchEmployeeId));
    }

    [Fact]
    public async Task Manager_cannot_access_other_branch_employee()
    {
        using var h = new Harness();
        Assert.False(await h.Access(h.ManagerId, EmployeeRole.Manager, h.OtherBranchEmployeeId));
    }

    [Fact]
    public async Task Manager_still_accesses_their_own_records()
    {
        using var h = new Harness();
        Assert.True(await h.Access(h.ManagerId, EmployeeRole.Manager, h.ManagerId));
    }

    [Fact]
    public async Task Admin_accesses_anyone_and_employee_only_self()
    {
        using var h = new Harness();
        Assert.True(await h.Access(h.SameBranchAdminId, EmployeeRole.Admin, h.OtherBranchEmployeeId));
        Assert.True(await h.Access(h.SameBranchEmployeeId, EmployeeRole.Employee, h.SameBranchEmployeeId));
        Assert.False(await h.Access(h.SameBranchEmployeeId, EmployeeRole.Employee, h.SameBranchAdminId));
    }

    // --- LocationScope: report/export summaries ----------------------------------

    [Fact]
    public async Task Manager_summary_scope_is_their_branches_and_stops_there()
    {
        // The tabel and the reports have to agree with the roster. While this excluded Role!=Employee
        // rows, a branch's timesheet was short by however many managers worked there and the totals
        // on two screens disagreed with no way to reconcile them.
        using var h = new Harness();
        h.Summary(h.SameBranchEmployeeId, h.BranchA);
        h.Summary(h.SameBranchAdminId, h.BranchA);   // same branch, above the manager — now visible
        h.Summary(h.ManagerId, h.BranchA);           // the manager's own row
        h.Summary(h.OtherBranchEmployeeId, h.BranchB); // another branch — must stay out

        var scoped = await LocationScope.ApplyLocationScopeAsync(
            h.Db, h.Db.DailySummaries, h.ManagerId, EmployeeRole.Manager, null, CancellationToken.None);

        Assert.Equal(ReportAccess.Allowed, scoped.Access);
        var ids = scoped.Query.Select(s => s.EmployeeId).ToList();
        Assert.Equal(
            new[] { h.ManagerId, h.SameBranchEmployeeId, h.SameBranchAdminId }.OrderBy(x => x),
            ids.OrderBy(x => x));
        Assert.DoesNotContain(h.OtherBranchEmployeeId, ids);
    }

    // --- Missed-checkout review: the write side ----------------------------------

    [Fact]
    public async Task Manager_pending_list_shows_only_their_employees_requests()
    {
        using var h = new Harness();
        h.Request(h.SameBranchEmployeeId);
        h.Request(h.SameBranchAdminId);  // hidden — above the manager
        h.Request(h.ManagerId);          // hidden — their own (no self-review)

        var ok = Assert.IsType<OkObjectResult>(await h.MissedCheckout(h.ManagerId, EmployeeRole.Manager).Pending());
        var names = ((IEnumerable)ok.Value!).Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("employeeName")!.GetValue(r)!)
            .ToList();
        Assert.Equal(new[] { "Filial İşçisi" }, names);

        // The admin still sees all three.
        var adminOk = Assert.IsType<OkObjectResult>(await h.MissedCheckout(h.SameBranchAdminId, EmployeeRole.Admin).Pending());
        Assert.Equal(3, ((IEnumerable)adminOk.Value!).Cast<object>().Count());
    }

    [Fact]
    public async Task Manager_cannot_approve_admins_or_their_own_request()
    {
        using var h = new Harness();
        var adminReq = h.Request(h.SameBranchAdminId);
        var ownReq = h.Request(h.ManagerId);
        var controller = h.MissedCheckout(h.ManagerId, EmployeeRole.Manager);

        foreach (var req in new[] { adminReq, ownReq })
        {
            var result = await controller.Approve(req.Id);
            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        }
        Assert.All(
            h.Db.MissedCheckoutRequests.AsNoTracking().ToList(),
            r => Assert.Equal(MissedCheckoutStatus.Pending, r.Status));
    }

    [Fact]
    public async Task Manager_can_approve_their_employees_request()
    {
        using var h = new Harness();
        var req = h.Request(h.SameBranchEmployeeId);
        var result = await h.MissedCheckout(h.ManagerId, EmployeeRole.Manager).Approve(req.Id);
        Assert.IsType<OkObjectResult>(result);
        var record = h.Db.AttendanceRecords.AsNoTracking().Single(r => r.EmployeeId == h.SameBranchEmployeeId);
        Assert.NotNull(record.CheckOutAtUtc); // the day actually closed
    }
}
