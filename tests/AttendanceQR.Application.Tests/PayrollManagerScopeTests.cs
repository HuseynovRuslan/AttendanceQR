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
/// The pay figures a MANAGER may see are their own branch's staff, and themselves.
///
/// Maaş was widened from Admin-only to managers because the branch manager is the person who knows why
/// somebody was away, and the deduction is computed from exactly that. But the scope the report is
/// built on deliberately carries EVERY role — it also feeds the boards, where a headcount that quietly
/// dropped the managers read short by one at a two-manager site. Inherited straight, that scope handed
/// a manager the salary of a peer manager and of the admin who clocks in at their gate.
///
/// So the money carries a second, narrower ceiling than the headcount: Role==Employee plus the caller.
/// These tests pin both halves — that the ceiling exists on the payroll, and that it was NOT achieved
/// by narrowing the shared location scope, which would silently shrink every board built on it.
///
/// A salary is the most sensitive number in this product and it is not recallable: once a manager has
/// read what the admin above them earns, no fix un-reads it.
/// </summary>
public class PayrollManagerScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c7");

    // A range that is entirely finished, so every row is read from DailySummaries and nothing is
    // computed live against the machine clock. A range touching today would make these amounts depend
    // on when the suite runs.
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 7);

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ReportQueryService Reports { get; }
        public Guid MyBranch { get; } = Guid.NewGuid();      // the manager's ManagedLocation
        public Guid OtherBranch { get; } = Guid.NewGuid();   // same tenant, not theirs
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid StaffOneId { get; } = Guid.NewGuid();
        public Guid StaffTwoId { get; } = Guid.NewGuid();
        public Guid PeerManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid OtherBranchStaffId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"payroll-scope-{Guid.NewGuid()}").Options,
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
                    LateThresholdMinutes = 15, QrVersion = 1, IsActive = true, WorkDaysMask = 127,
                });

            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = MyBranch, TenantId = TenantId,
            });

            // Distinct salaries, all divisible by the 7 scheduled days in the range, so every amount
            // below is exact and a single leaked row moves the totals by a recognisable number.
            Db.Employees.Add(Person(ManagerId, "Menecer Ozu", EmployeeRole.Manager, MyBranch, 1400m));
            Db.Employees.Add(Person(StaffOneId, "Isci Bir", EmployeeRole.Employee, MyBranch, 700m));
            Db.Employees.Add(Person(StaffTwoId, "Isci Iki", EmployeeRole.Employee, MyBranch, 700m));
            Db.Employees.Add(Person(PeerManagerId, "Ikinci Menecer", EmployeeRole.Manager, MyBranch, 2100m));
            // Placed at the manager's OWN branch on purpose: an admin carries a LocationId too (it is
            // where they clock in), so branch membership alone would price the person above the manager.
            Db.Employees.Add(Person(SameBranchAdminId, "Sirket Admini", EmployeeRole.Admin, MyBranch, 2800m));
            Db.Employees.Add(Person(OtherBranchStaffId, "Yad Filial Iscisi", EmployeeRole.Employee, OtherBranch, 700m));
            Db.SaveChanges();

            // The report reads finished days from DailySummaries; with the table empty every row would
            // be missing for the harmless reason that there is no data, and the scope would prove
            // nothing. Five worked days and two unexcused absences each: divisor 7, so a 700 salary
            // deducts 200 and pays 500.
            foreach (var e in Db.Employees.ToList())
            {
                var branch = e.LocationId;
                for (var d = From; d <= To; d = d.AddDays(1))
                    Db.DailySummaries.Add(new DailySummary
                    {
                        TenantId = TenantId, EmployeeId = e.Id, LocationId = branch, SummaryDate = d,
                        Status = d.Day <= 5 ? DailySummaryStatus.OnTime : DailySummaryStatus.Absent,
                        WorkedMinutes = d.Day <= 5 ? 480 : 0,
                    });
            }
            Db.SaveChanges();

            Reports = new ReportQueryService(Db, new AppOptions());
        }

        private Employee Person(Guid id, string name, EmployeeRole role, Guid location, decimal salary) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true,
            PasswordHash = "h", LocationId = location, MonthlySalary = salary,
            ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        public Task<(ReportAccess Access, PayrollReport? Report)> PayrollAsync(
            Guid who, EmployeeRole role, Guid? locationId = null)
            => Reports.GetPayrollAsync(From, To, locationId, who, role);

        public Task<(ReportAccess Access, AttendanceReport? Report)> SummaryAsync(Guid who, EmployeeRole role)
            => Reports.GetSummaryAsync(From, To, null, who, role);

        public void Dispose() => Db.Dispose();
    }

    private static List<Guid> IdsOf(PayrollReport report) => report.Rows.Select(r => r.EmployeeId).ToList();

    [Fact]
    public async Task A_managers_payroll_lists_their_own_branch_staff()
    {
        using var h = new Harness();

        var (access, report) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager);

        Assert.Equal(ReportAccess.Allowed, access);
        var ids = IdsOf(report!);
        Assert.Contains(h.StaffOneId, ids);
        Assert.Contains(h.StaffTwoId, ids);
    }

    [Fact]
    public async Task A_managers_payroll_includes_their_own_line()
    {
        // The one exception to the Role==Employee ceiling, and it is deliberate: a manager who opens
        // Maaş and cannot find themselves reads it as their own pay being kept from them.
        using var h = new Harness();

        var (_, report) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager);

        var mine = Assert.Single(report!.Rows, r => r.EmployeeId == h.ManagerId);
        Assert.Equal(1400m, mine.MonthlySalary);
        Assert.Equal(1000m, mine.Payable); // 1400 less (1400/7 per day, twice) for the two absences
    }

    [Fact]
    public async Task A_managers_payroll_does_not_price_a_peer_manager_or_a_same_branch_admin()
    {
        // The defect this file exists for. Both people sit inside the manager's location scope and
        // always will — the scope is shared with the boards, where omitting them was itself the bug.
        using var h = new Harness();

        var (_, report) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager);

        var ids = IdsOf(report!);
        Assert.DoesNotContain(h.PeerManagerId, ids);
        Assert.DoesNotContain(h.SameBranchAdminId, ids);
    }

    [Fact]
    public async Task The_totals_carry_only_the_visible_rows()
    {
        // A row can be filtered out of the table and still be summed into the footer, which leaks the
        // same number more quietly: the admin's 2800 would arrive as a company total nobody could
        // account for from the lines above it.
        using var h = new Harness();

        var (_, report) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager);

        Assert.Equal(3, report!.Rows.Count);              // two staff + the manager
        Assert.Equal(2800m, report.TotalMonthlySalary);   // 700 + 700 + 1400, not 7700
        Assert.Equal(800m, report.TotalDeduction);
        Assert.Equal(2000m, report.TotalPayable);
        Assert.Equal(report.Rows.Sum(r => r.Payable), report.TotalPayable);
    }

    [Fact]
    public async Task A_manager_cannot_reach_another_branch_by_asking_for_it()
    {
        // Naming a branch they do not manage is refused outright rather than quietly answered with an
        // empty table — an empty table reads as "nobody over there is owed anything".
        using var h = new Harness();

        var (access, report) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager, h.OtherBranch);

        Assert.Equal(ReportAccess.Forbidden, access);
        Assert.Null(report);
    }

    [Fact]
    public async Task Another_branchs_staff_are_absent_from_an_unfiltered_managers_payroll()
    {
        using var h = new Harness();

        var (_, report) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager);

        Assert.DoesNotContain(h.OtherBranchStaffId, IdsOf(report!));
    }

    [Fact]
    public async Task The_other_branch_is_hidden_by_scope_not_by_missing_data()
    {
        // Without this control the previous test would keep passing if the seed broke and that branch
        // simply had no priced days at all.
        using var h = new Harness();

        var (access, report) = await h.PayrollAsync(h.SameBranchAdminId, EmployeeRole.Admin, h.OtherBranch);

        Assert.Equal(ReportAccess.Allowed, access);
        var row = Assert.Single(report!.Rows);
        Assert.Equal(h.OtherBranchStaffId, row.EmployeeId);
        Assert.Equal(500m, row.Payable);
    }

    [Fact]
    public async Task An_admins_payroll_still_contains_everyone()
    {
        // The other half of the widening: the ceiling belongs to the MANAGER, and an admin who lost the
        // managers' and their own lines from Maaş would be a payroll that cannot pay the payroll.
        using var h = new Harness();

        var (access, report) = await h.PayrollAsync(h.SameBranchAdminId, EmployeeRole.Admin);

        Assert.Equal(ReportAccess.Allowed, access);
        var ids = IdsOf(report!);
        Assert.Equal(6, ids.Count);
        Assert.Contains(h.ManagerId, ids);
        Assert.Contains(h.PeerManagerId, ids);
        Assert.Contains(h.SameBranchAdminId, ids);
        Assert.Contains(h.OtherBranchStaffId, ids);
        Assert.Equal(8400m, report.TotalMonthlySalary);
    }

    [Fact]
    public async Task The_headcount_report_still_counts_the_peer_manager_and_the_admin()
    {
        // The seam. The obvious way to "fix" the leak is to narrow the shared location scope to
        // Role==Employee — and that would take the managers back out of every board, tabel and
        // attendance total built on it, which is the defect that scope was widened to cure. The pay
        // ceiling has to sit on the payroll alone, so the same caller sees five people and three
        // salaries.
        using var h = new Harness();

        var (_, headcount) = await h.SummaryAsync(h.ManagerId, EmployeeRole.Manager);
        var (_, payroll) = await h.PayrollAsync(h.ManagerId, EmployeeRole.Manager);

        var seen = headcount!.Rows.Select(r => r.EmployeeId).ToList();
        Assert.Equal(5, seen.Count); // two staff + the manager + a peer manager + the branch admin
        Assert.Contains(h.PeerManagerId, seen);
        Assert.Contains(h.SameBranchAdminId, seen);
        Assert.Equal(3, payroll!.Rows.Count);
    }
}
