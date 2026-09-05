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
/// «Çıxış etməmisiniz» must mean a day that ENDED without a check-out — never a shift in progress.
///
/// Reported from a phone: a worker clocked in at 16:18 and «Saatlarım» immediately answered with a red
/// «1 gün çıxış etməmisiniz — bu günlər 0 saat sayılıb», about the shift they were standing in the
/// middle of. The rule already existed one method away (GetDashboardAsync draws exactly this
/// distinction, and says so in its own comment); the report those screens share never applied it.
///
/// Both halves matter and they pull in opposite directions: today must stay inside WorkDays — the day
/// IS being worked, and the green «1 gün» on that screen is right — while staying out of
/// IncompleteDays until it is over.
/// </summary>
public class StillAtWorkTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f7");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ReportQueryService Reports { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid BranchId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"still-at-work-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = BranchId, TenantId = TenantId, Name = "Merkez",
                Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true, WorkDaysMask = 127,
            });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Isci", Role = EmployeeRole.Employee,
                IsActive = true, PasswordHash = "h", LocationId = BranchId,
                ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            Db.SaveChanges();

            Reports = new ReportQueryService(Db, new AppOptions());
        }

        /// <summary>A finished day with a check-in and no check-out — a real forgotten check-out.</summary>
        public void OpenSummaryOn(DateOnly date)
        {
            Db.DailySummaries.Add(new DailySummary
            {
                TenantId = TenantId, EmployeeId = EmployeeId, LocationId = BranchId,
                SummaryDate = date, Status = DailySummaryStatus.Incomplete,
            });
            Db.SaveChanges();
        }

        /// <summary>A live, still-running shift: checked in today, no check-out yet.</summary>
        public void CheckedInTodayOn(DateOnly today)
        {
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LocationId = BranchId,
                AttendanceDate = today,
                CheckInAtUtc = DateTime.UtcNow.AddHours(-1),
                Status = AttendanceStatus.OnTime,
            });
            Db.SaveChanges();
        }

        public Task<(ReportAccess Access, AttendanceReport? Report)> MineAsync(DateOnly from, DateOnly to)
            => Reports.GetSummaryAsync(from, to, null, EmployeeId, EmployeeRole.Employee);

        public void Dispose() => Db.Dispose();
    }

    // The company clock, which is what the report compares dates against.
    private static DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(new AppOptions().TimeZone)));

    [Fact]
    public async Task A_shift_still_running_today_is_NOT_a_missed_check_out()
    {
        // The reported bug, as a number: clock in, open «Saatlarım», be told you forgot to leave.
        using var h = new Harness();
        var today = Today();
        h.CheckedInTodayOn(today);

        var (_, report) = await h.MineAsync(today, today);

        Assert.Equal(0, report!.Totals.IncompleteDays);
    }

    [Fact]
    public async Task But_today_still_counts_as_a_day_worked()
    {
        // The other half — and the reason this cannot be fixed by dropping today from the rows: the
        // green «Aylıq işlənmiş gün» on the same screen was right all along.
        using var h = new Harness();
        var today = Today();
        h.CheckedInTodayOn(today);

        var (_, report) = await h.MineAsync(today, today);

        Assert.Equal(1, report!.Totals.WorkDays);
    }

    [Fact]
    public async Task A_FINISHED_day_with_no_check_out_is_still_reported()
    {
        // The guard: this is a real forgotten check-out, it really does score zero hours, and the
        // person really does need telling. Yesterday, so the day is over.
        using var h = new Harness();
        var today = Today();
        var yesterday = today.AddDays(-1);
        h.OpenSummaryOn(yesterday);

        var (_, report) = await h.MineAsync(yesterday, yesterday);

        Assert.Equal(1, report!.Totals.IncompleteDays);
    }

    [Fact]
    public async Task Yesterdays_forgotten_check_out_survives_a_range_that_includes_today()
    {
        // Both rules at once, which is where an "exclude today" fix most easily goes wrong: the open
        // day before is counted, the one in progress is not.
        using var h = new Harness();
        var today = Today();
        h.OpenSummaryOn(today.AddDays(-1));
        h.CheckedInTodayOn(today);

        var (_, report) = await h.MineAsync(today.AddDays(-1), today);

        Assert.Equal(1, report!.Totals.IncompleteDays);
        Assert.Equal(2, report.Totals.WorkDays);
    }
}
