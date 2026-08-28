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
/// The figures on a person's OWN profile screen are their own.
///
/// That screen called the role-scoped report — an employee gets themselves, a manager their branches,
/// an admin the whole company — so an admin's personal card showed the company: 947 days worked, 7,893
/// hours, and a banner reading "601 days absent this month". Reported from Green Garden, 2026-08-28.
///
/// The wrong number is only half of it. The banner it feeds says "if you think this is a mistake, tell
/// your manager — it can be corrected", so the screen invites somebody to chase an absence that was
/// never theirs, in a product whose entire job is agreeing on who worked which day.
///
/// The scope switch already had the right branch. What was missing was refusing to let the caller's
/// role choose a wider one, which is what these pin.
/// </summary>
public class MySummaryScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e4");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ReportQueryService Reports { get; }
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid BranchId { get; } = Guid.NewGuid();
        public List<Guid> StaffIds { get; } = [];

        public Harness(int staff = 5)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"my-summary-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true,
            });
            Db.Locations.Add(new Location
            {
                Id = BranchId, TenantId = TenantId, Name = "Merkez",
                Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
                // Every day is a working day, so an employee with no record is Absent — the figure the
                // broken screen was aggregating.
                WorkDaysMask = 127,
            });

            Db.Employees.Add(Person(AdminId, "Sirket Admini", EmployeeRole.Admin));
            Db.Employees.Add(Person(ManagerId, "Filial Meneceri", EmployeeRole.Manager));
            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = BranchId, TenantId = TenantId,
            });

            for (var i = 0; i < staff; i++)
            {
                var id = Guid.NewGuid();
                StaffIds.Add(id);
                Db.Employees.Add(Person(id, $"Isci {i}", EmployeeRole.Employee));
            }

            Db.SaveChanges();

            // A finished day is read from DailySummaries, so the range has to be closed for the report
            // to have anything to scope. Everyone absent every day: the shape that produced the
            // reported figure, where one person's card carried everybody's absences.
            foreach (var e in Db.Employees.ToList())
                for (var d = From; d <= To; d = d.AddDays(1))
                    Db.DailySummaries.Add(new DailySummary
                    {
                        TenantId = TenantId, EmployeeId = e.Id, LocationId = BranchId,
                        SummaryDate = d, Status = DailySummaryStatus.Absent,
                    });
            Db.SaveChanges();

            Reports = new ReportQueryService(Db, new AppOptions());
        }

        private Employee Person(Guid id, string name, EmployeeRole role) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true,
            PasswordHash = "h", LocationId = BranchId,
            ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        /// <summary>What the profile screen asks for: always the caller, whatever their role.</summary>
        public Task<(ReportAccess Access, AttendanceReport? Report)> MineAsync(Guid who)
            => Reports.GetSummaryAsync(From, To, null, who, EmployeeRole.Employee);

        /// <summary>What the admin REPORTS screen asks for — role-scoped, and still must be.</summary>
        public Task<(ReportAccess Access, AttendanceReport? Report)> ByRoleAsync(Guid who, EmployeeRole role)
            => Reports.GetSummaryAsync(From, To, null, who, role);

        public void Dispose() => Db.Dispose();
    }

    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 7);

    [Fact]
    public async Task An_admins_own_card_counts_one_person()
    {
        // The bug, stated as a number: seven employees over seven days is what the admin was shown as
        // their personal figure.
        using var h = new Harness(staff: 5);

        var (access, report) = await h.MineAsync(h.AdminId);

        Assert.Equal(ReportAccess.Allowed, access);
        Assert.Single(report!.Rows);
        Assert.Equal("Sirket Admini", report.Rows[0].EmployeeName);
    }

    [Fact]
    public async Task A_managers_own_card_counts_one_person()
    {
        using var h = new Harness(staff: 5);

        var (_, report) = await h.MineAsync(h.ManagerId);

        Assert.Single(report!.Rows);
        Assert.Equal("Filial Meneceri", report.Rows[0].EmployeeName);
    }

    [Fact]
    public async Task The_absence_figure_is_the_persons_own()
    {
        // The banner the wrong number fed. Seven staff each absent for seven working days summed into
        // one person's card as "601 days absent this month" at the real company's size.
        using var h = new Harness(staff: 5);

        var (_, mine) = await h.MineAsync(h.AdminId);
        var (_, company) = await h.ByRoleAsync(h.AdminId, EmployeeRole.Admin);

        // Seven days in the range, and this admin never scanned — so their own figure is seven, not
        // the company's seven-times-seven.
        Assert.Equal(7, mine!.Totals.AbsentDays);
        Assert.Equal(49, company!.Totals.AbsentDays);
    }

    [Fact]
    public async Task An_ordinary_employee_sees_exactly_what_they_saw_before()
    {
        // Nothing changes for the people the screen was already right for.
        using var h = new Harness(staff: 3);

        var (_, mine) = await h.MineAsync(h.StaffIds[0]);
        var (_, byRole) = await h.ByRoleAsync(h.StaffIds[0], EmployeeRole.Employee);

        Assert.Single(mine!.Rows);
        Assert.Equal(mine.Totals.AbsentDays, byRole!.Totals.AbsentDays);
    }

    [Fact]
    public async Task The_admin_reports_screen_still_sees_the_company()
    {
        // The other half: narrowing the profile card must not have narrowed the reports.
        using var h = new Harness(staff: 5);

        var (_, report) = await h.ByRoleAsync(h.AdminId, EmployeeRole.Admin);

        Assert.Equal(7, report!.Rows.Count); // 5 staff + manager + admin
    }
}
