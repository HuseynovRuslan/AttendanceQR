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
/// İstirahət is not məzuniyyət, and the timesheet has to say so.
///
/// The owner's report, in his words: «bu məzuniyyəti istirahətə qarışdırmağın bezdirdi məni — bir
/// yerdə düzəldirəm, bir yerdə yenə çıxır». The literal instance was here. `LeaveCodeFor` mapped
/// five of the six leave types and sent everything else to its default, «M» — and the only real
/// value that could reach that default was Rest. The path is not exotic: for anyone still inside the
/// onboarding window (activated, never scanned — 227 people on production the day this was found) a
/// granted rest day printed «M» in the timesheet the accountant reconciles, and was added to that
/// row's leave tally. One day off became one day of annual leave.
///
/// The other half is the same confusion read backwards: outside that window a granted rest printed
/// «H», the code for the roster's own day off, so a decision a manager took was indistinguishable
/// from an ordinary Sunday and could never be found again.
///
/// These tests run the real <see cref="ReportQueryService.GetTabelAsync"/>. The test that existed
/// before re-implemented the rule in a local helper and passed throughout — which is why it caught
/// nothing.
/// </summary>
public class TabelRestIsNotLeaveTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e3");
    private const int Year = 2026, Month = 3;   // a settled month in the past

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ReportQueryService Reports { get; }
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid BranchId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"tabel-rest-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = BranchId, TenantId = TenantId, Name = "Merkez",
                Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
                WorkDaysMask = 127,   // every day is a working day, so an «H» can only be a granted rest
            });
            Db.Employees.Add(new Employee
            {
                Id = AdminId, TenantId = TenantId, FullName = "Admin", Role = EmployeeRole.Admin,
                IsActive = true, PasswordHash = "h", LocationId = BranchId,
                ActivatedAtUtc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            Db.SaveChanges();
            Reports = new ReportQueryService(Db, new AppOptions());
        }

        /// <param name="everScanned">
        /// The onboarding guard turns on precisely for somebody activated who has never recorded
        /// attendance — which is the population the «M» bug lived in.
        /// </param>
        public Guid AddWorker(string name, bool everScanned)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = name, Role = EmployeeRole.Employee,
                IsActive = true, PasswordHash = "h", LocationId = BranchId,
                ActivatedAtUtc = new DateTime(Year, Month, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(Year, Month, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            if (everScanned)
            {
                // One finished day, well before the days under test, so the person counts as started.
                var date = new DateOnly(Year, Month, 2);
                Db.AttendanceRecords.Add(new AttendanceRecord
                {
                    TenantId = TenantId, EmployeeId = id, LocationId = BranchId, AttendanceDate = date,
                    CheckInAtUtc = date.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc),
                    CheckOutAtUtc = date.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc),
                });
                Db.DailySummaries.Add(new DailySummary
                {
                    TenantId = TenantId, EmployeeId = id, LocationId = BranchId, SummaryDate = date,
                    Status = DailySummaryStatus.OnTime, WorkedMinutes = 540,
                });
            }
            Db.SaveChanges();
            return id;
        }

        public void GiveLeave(Guid employeeId, int day, LeaveType type)
        {
            var date = new DateOnly(Year, Month, day);
            Db.LeaveRecords.Add(new LeaveRecord
            {
                TenantId = TenantId, EmployeeId = employeeId, FromDate = date, ToDate = date,
                Type = type, CreatedByEmployeeId = AdminId,
            });
            // What the nightly job would have written for a day with no scan and a leave over it.
            Db.DailySummaries.Add(new DailySummary
            {
                TenantId = TenantId, EmployeeId = employeeId, LocationId = BranchId, SummaryDate = date,
                Status = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay: true, type),
            });
            Db.SaveChanges();
        }

        public async Task<TabelRow> RowFor(Guid employeeId)
        {
            var (access, report) = await Reports.GetTabelAsync(Year, Month, null, AdminId, EmployeeRole.Admin);
            Assert.Equal(ReportAccess.Allowed, access);
            return report!.Rows.Single(r => r.EmployeeId == employeeId);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task A_rest_day_never_prints_the_holiday_code_not_even_before_the_first_scan()
    {
        // THE bug. Before the fix this cell was «M» and the day joined the row's leave total.
        using var h = new Harness();
        var worker = h.AddWorker("Hec skan etmeyib", everScanned: false);
        h.GiveLeave(worker, 10, LeaveType.Rest);

        var row = await h.RowFor(worker);

        Assert.Equal("İs", row.Days[9]);
        Assert.NotEqual("M", row.Days[9]);
        Assert.Equal(0, row.LeaveDays);
    }

    [Fact]
    public async Task A_granted_rest_is_told_apart_from_the_calendars_own_day_off()
    {
        // The mirror of the same confusion: outside the onboarding window a granted rest used to
        // print «H», the roster's own code, so the decision vanished into the weekend.
        using var h = new Harness();
        var worker = h.AddWorker("Isleyir", everScanned: true);
        h.GiveLeave(worker, 11, LeaveType.Rest);

        var row = await h.RowFor(worker);

        Assert.Equal("İs", row.Days[10]);
        // Day 12 has nothing on it. The branch works every day, so it is an absence, not an «H» —
        // what matters is only that it did not borrow the granted day's code.
        Assert.NotEqual("İs", row.Days[11]);
    }

    [Fact]
    public async Task Every_other_leave_type_still_prints_its_own_code()
    {
        // The default arm that swallowed Rest is still there for a genuinely unknown value; nothing
        // above it may have been disturbed.
        using var h = new Harness();
        var worker = h.AddWorker("Novleri", everScanned: false);
        h.GiveLeave(worker, 3, LeaveType.Vacation);
        h.GiveLeave(worker, 4, LeaveType.Sick);
        h.GiveLeave(worker, 5, LeaveType.Unpaid);
        h.GiveLeave(worker, 6, LeaveType.Permission);
        h.GiveLeave(worker, 7, LeaveType.BusinessTrip);

        var row = await h.RowFor(worker);

        Assert.Equal("M", row.Days[2]);
        Assert.Equal("X", row.Days[3]);
        Assert.Equal("ÖM", row.Days[4]);
        Assert.Equal("İC", row.Days[5]);
        Assert.Equal("Ez", row.Days[6]);
        // Five leave days, and the rest day of the other tests is not among them.
        Assert.Equal(5, row.LeaveDays);
    }

    [Fact]
    public async Task The_legend_names_both_kinds_of_day_off()
    {
        // A legend that omits a code the grid emits is how «İs» becomes a letter nobody can read.
        using var h = new Harness();
        h.AddWorker("Bir", everScanned: true);

        var (_, report) = await h.Reports.GetTabelAsync(Year, Month, null, h.AdminId, EmployeeRole.Admin);

        var codes = report!.Legend.Select(l => l.Code).ToList();
        Assert.Contains("İs", codes);
        Assert.Contains("H", codes);
        Assert.Equal("İstirahət (təyin edilmiş)", report.Legend.Single(l => l.Code == "İs").Label);
        Assert.Equal("Həftəlik istirahət", report.Legend.Single(l => l.Code == "H").Label);
    }

    [Fact]
    public async Task A_holiday_does_not_swallow_a_day_somebody_was_granted_off()
    {
        // The rest day was lost twice over: first into the weekend, then into the bayram pass that
        // recolours «H» cells. Only the calendar's own day off may become «B».
        using var h = new Harness();
        var worker = h.AddWorker("Bayramda", everScanned: true);
        h.GiveLeave(worker, 8, LeaveType.Rest);
        h.Db.NonWorkingDays.Add(new NonWorkingDay
        {
            TenantId = TenantId, Date = new DateOnly(Year, Month, 8), Description = "Bayram",
        });
        h.Db.SaveChanges();

        var row = await h.RowFor(worker);

        Assert.Equal("İs", row.Days[7]);
    }
}
