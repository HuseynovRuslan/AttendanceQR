using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The branch a period report files somebody under, when their days in the period sit under two branches.
///
/// Each DailySummary carries the branch the person was at that day, so a mid-month move leaves the month's rows
/// split. The summary report gives each person one branch label, and it was the first row's — but the rows are
/// read from DailySummaries with no ORDER BY, and Postgres returns them in whatever order the plan and the
/// table's layout produce (every rebuild of a day rewrites its row elsewhere). The same month exported twice
/// could put the person under the old branch, then the new one; the payroll copies the label and the summary
/// Excel adds its per-branch lines up by it, so branch totals did not reconcile between two downloads.
///
/// The label is now the branch of the person's LATEST day in the range. The in-memory provider returns rows in the
/// order they were added, so each test runs with the days added both ways round: oldest first (what the old code
/// read back here, and the case that failed) and newest first.
/// </summary>
public class ReportBranchLabelTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000b7a1");

    // Entirely finished, so every row comes from DailySummaries and nothing is computed live against the clock.
    private static readonly DateOnly From = new(2026, 6, 1);
    private static readonly DateOnly To = new(2026, 6, 7);
    private static readonly DateOnly MovedOn = new(2026, 6, 4);

    private const string OldName = "Köhnə filial";
    private const string NewName = "Yeni filial";

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ReportQueryService Reports { get; }
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid MovedId { get; } = Guid.NewGuid();
        public Guid StayedId { get; } = Guid.NewGuid();

        public Harness(bool newestFirst)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"report-branch-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });

            var oldBranch = Guid.NewGuid();
            var newBranch = Guid.NewGuid();
            foreach (var (id, name) in new[] { (oldBranch, OldName), (newBranch, NewName) })
                Db.Locations.Add(new Location
                {
                    Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15,
                    QrVersion = 1, IsActive = true, WorkDaysMask = 127,
                });

            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, oldBranch));
            Db.Employees.Add(Person(MovedId, "Köçən İşçi", EmployeeRole.Employee, newBranch));    // moved on the 4th
            Db.Employees.Add(Person(StayedId, "Qalan İşçi", EmployeeRole.Employee, oldBranch));
            Db.SaveChanges();

            var days = Enumerable.Range(0, To.DayNumber - From.DayNumber + 1).Select(From.AddDays).ToList();
            if (newestFirst) days.Reverse();
            foreach (var d in days)
            {
                Db.DailySummaries.Add(Worked(MovedId, d < MovedOn ? oldBranch : newBranch, d));
                Db.DailySummaries.Add(Worked(StayedId, oldBranch, d));
            }
            Db.SaveChanges();

            Reports = new ReportQueryService(Db, new AppOptions());
        }

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid location) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true, PasswordHash = "h",
            LocationId = location, MonthlySalary = 700m,
            ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        private static DailySummary Worked(Guid employeeId, Guid locationId, DateOnly date) => new()
        {
            TenantId = TenantId, EmployeeId = employeeId, LocationId = locationId, SummaryDate = date,
            Status = DailySummaryStatus.OnTime, WorkedMinutes = 480,
        };

        public void Dispose() => Db.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_person_moved_mid_period_is_filed_under_the_branch_of_their_latest_day(bool newestFirst)
    {
        using var h = new Harness(newestFirst);

        var (access, report) = await h.Reports.GetSummaryAsync(From, To, null, h.AdminId, EmployeeRole.Admin);

        Assert.Equal(ReportAccess.Allowed, access);
        Assert.Equal(NewName, report!.Rows.Single(r => r.EmployeeId == h.MovedId).LocationName);
        Assert.Equal(OldName, report.Rows.Single(r => r.EmployeeId == h.StayedId).LocationName);
        // The label moves nothing else: all seven days are still theirs.
        Assert.Equal(7, report.Rows.Single(r => r.EmployeeId == h.MovedId).WorkDays);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_payroll_carries_the_same_branch(bool newestFirst)
    {
        using var h = new Harness(newestFirst);

        var (_, payroll) = await h.Reports.GetPayrollAsync(From, To, null, h.AdminId, EmployeeRole.Admin);

        Assert.Equal(NewName, payroll!.Rows.Single(r => r.EmployeeId == h.MovedId).LocationName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_summary_sheet_adds_each_branch_up_the_same_way_every_time(bool newestFirst)
    {
        using var h = new Harness(newestFirst);
        var (_, report) = await h.Reports.GetSummaryAsync(From, To, null, h.AdminId, EmployeeRole.Admin);

        using var book = new XLWorkbook(new MemoryStream(new ExcelReportExporter().Build(report!)));
        var sheet = book.Worksheet("Xülasə");
        int Headcount(string branch) =>
            sheet.RowsUsed().Where(r => r.Cell(1).GetString() == branch).Select(r => r.Cell(2).GetValue<int>()).Single();

        // One line per branch: the person who stayed, and the person who moved — under where they ended up.
        // (The admin has no days in the range and no line of their own.)
        Assert.Equal(1, Headcount(OldName));
        Assert.Equal(1, Headcount(NewName));
    }
}
