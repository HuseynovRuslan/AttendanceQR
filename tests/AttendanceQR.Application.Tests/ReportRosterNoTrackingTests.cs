using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The roster behind the today board and the dashboard — read, counted, and thrown away.
///
/// Every live screen in the admin panel resolves «the active employees this caller may see» before it
/// computes anything, and the today board polls every thirty seconds. That list was tracked: the
/// change tracker took a snapshot of nine hundred entities, on every poll, for a list that is never
/// written back. Tracking is the cost; it is also a correctness hazard on a long-lived context, since
/// a tracked entity shadows what a later query reads.
///
/// What must NOT change is who is on that list. These tests pin both: the tracker stays empty, and
/// the same people come back — including the manager boundary, which is the one thing in this whole
/// change that is a permission rather than a performance question.
/// </summary>
public class ReportRosterNoTrackingTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000c003");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid BranchA { get; } = Guid.NewGuid();
        public Guid BranchB { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public DateOnly Today { get; }

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"roster-track-{Guid.NewGuid()}").Options,
                tenant);
            Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku));

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            foreach (var (id, name) in new[] { (BranchA, "Mərkəz"), (BranchB, "Novxanı") })
                Db.Locations.Add(new Location
                {
                    Id = id, TenantId = TenantId, Name = name,
                    Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                    LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
                });

            Person(AdminId, "Şirkət Admini", BranchA, EmployeeRole.Admin);
            Person(ManagerId, "Mərkəz Meneceri", BranchA, EmployeeRole.Manager);
            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantId,
            });
            Db.SaveChanges();
        }

        public Guid Person(Guid? id, string name, Guid branch, EmployeeRole role = EmployeeRole.Employee)
        {
            var eid = id ?? Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = eid, TenantId = TenantId, FullName = name, LocationId = branch, Role = role,
                IsActive = true, PasswordHash = "x", ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            // One real day behind them: somebody who has never recorded any attendance is never judged.
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = eid, LocationId = branch,
                AttendanceDate = Today.AddDays(-20),
                CheckInAtUtc = DateTime.UtcNow.AddDays(-20),
                CheckOutAtUtc = DateTime.UtcNow.AddDays(-20).AddHours(8),
            });
            Db.SaveChanges();
            return eid;
        }

        public ReportQueryService Service() => new(Db, new AppOptions { TimeZone = "Asia/Baku" });

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task The_today_board_leaves_nothing_in_the_change_tracker()
    {
        using var h = new Harness();
        for (var i = 0; i < 12; i++) h.Person(null, $"İşçi {i:D2}", i % 2 == 0 ? h.BranchA : h.BranchB);
        // The seeding tracked its own inserts; from here on, anything in the tracker came out of the
        // query under test.
        h.Db.ChangeTracker.Clear();

        var board = await h.Service().GetTodayAttendanceAsync(h.AdminId, EmployeeRole.Admin);

        Assert.Equal(14, board.Count);
        Assert.Empty(h.Db.ChangeTracker.Entries<Employee>());
        Assert.Empty(h.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task And_so_does_the_dashboard()
    {
        using var h = new Harness();
        for (var i = 0; i < 8; i++) h.Person(null, $"İşçi {i:D2}", h.BranchA);
        h.Db.ChangeTracker.Clear();

        var (access, report) = await h.Service()
            .GetDashboardAsync(h.Today, h.Today, null, h.AdminId, EmployeeRole.Admin);

        Assert.Equal(ReportAccess.Allowed, access);
        Assert.NotNull(report);
        Assert.Empty(h.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task An_admin_still_sees_the_whole_company_and_a_branch_filter_still_narrows_it()
    {
        using var h = new Harness();
        for (var i = 0; i < 5; i++) h.Person(null, $"A {i}", h.BranchA);
        for (var i = 0; i < 3; i++) h.Person(null, $"B {i}", h.BranchB);

        var all = await h.Service().GetTodayAttendanceAsync(h.AdminId, EmployeeRole.Admin);
        var (access, branchB) = await h.Service()
            .GetSummaryAsync(h.Today, h.Today, h.BranchB, h.AdminId, EmployeeRole.Admin);

        // Admin + manager + 8 staff; admins and managers who clock in ARE on the board.
        Assert.Equal(10, all.Count);
        Assert.Equal(ReportAccess.Allowed, access);
        Assert.Equal(3, branchB!.Rows.Count);
    }

    [Fact]
    public async Task A_manager_still_sees_only_their_own_branch()
    {
        // The boundary that is a permission, not a performance question. A manager sees the branches
        // they manage and nothing else, and asking for somebody else's branch is refused outright.
        using var h = new Harness();
        for (var i = 0; i < 5; i++) h.Person(null, $"A {i}", h.BranchA);
        for (var i = 0; i < 3; i++) h.Person(null, $"B {i}", h.BranchB);

        var mine = await h.Service().GetTodayAttendanceAsync(h.ManagerId, EmployeeRole.Manager);

        // Their own branch: the admin who clocks in there, themselves, and five staff.
        Assert.Equal(7, mine.Count);
        Assert.DoesNotContain(mine, r => r.LocationName == "Novxanı");
    }

    [Fact]
    public async Task An_employee_still_sees_only_themselves()
    {
        using var h = new Harness();
        var me = h.Person(null, "Tək Mən", h.BranchA);
        for (var i = 0; i < 4; i++) h.Person(null, $"Başqası {i}", h.BranchA);

        var board = await h.Service().GetTodayAttendanceAsync(me, EmployeeRole.Employee);

        Assert.Equal(me, Assert.Single(board).EmployeeId);
    }
}
