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
/// «Yalnız panel» (Employee.PanelOnly) — a manager account in a company whose attendance is not his.
///
/// The case: a site supervisor moved to another company with his history keeps a manager account at
/// his old site to watch its crew. He clocks in where he was moved to. In the old company he must not
/// be a row on the board, a line in the tabel, or one more in the management report's «İşçi sayı».
///
/// Both halves of a day are pinned, because they answer «who» separately: the live board
/// (ScopedEmployeesAsync) and the finished day (DailySummaryService). One left behind and the day's
/// totals change at midnight.
/// </summary>
public class PanelOnlyAccountTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d4");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid BranchId { get; } = Guid.NewGuid();
        public Guid WorkerId { get; } = Guid.NewGuid();
        public Guid PanelManagerId { get; } = Guid.NewGuid();
        public DateOnly Yesterday { get; } =
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku)).AddDays(-1);

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"panel-only-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "C", Slug = "c", DisplayName = "C", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = BranchId, TenantId = TenantId, Name = "Sahə",
                Latitude = 40.4, Longitude = 49.8, RadiusMeters = 300,
                ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            var activated = DateTime.UtcNow.AddDays(-30);
            Db.Employees.Add(new Employee
            {
                Id = WorkerId, TenantId = TenantId, FullName = "Işçi", LocationId = BranchId,
                Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x", ActivatedAtUtc = activated,
            });
            Db.Employees.Add(new Employee
            {
                Id = PanelManagerId, TenantId = TenantId, FullName = "Panel meneceri", LocationId = BranchId,
                Role = EmployeeRole.Manager, IsActive = true, PasswordHash = "x", ActivatedAtUtc = activated,
                PanelOnly = true,
            });
            Db.SaveChanges();
        }

        /// <summary>«Qayıb yaz» for yesterday — the one thing that writes a summary for somebody who never scans.</summary>
        public void MarkAbsentYesterday(Guid employeeId)
        {
            Db.AbsenceMarks.Add(new AbsenceMark { TenantId = TenantId, EmployeeId = employeeId, Date = Yesterday });
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task The_board_lists_the_crew_and_not_the_panel_account()
    {
        using var h = new Harness();
        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" });

        var rows = await reports.GetBoardForCompanyAsync();

        Assert.Contains(rows, r => r.EmployeeId == h.WorkerId);
        Assert.DoesNotContain(rows, r => r.EmployeeId == h.PanelManagerId);
    }

    [Fact]
    public async Task The_finished_day_is_written_for_the_crew_and_not_for_the_panel_account()
    {
        // Both are marked away — the same evidence. Only the worker is somebody this company keeps
        // days for; the panel account gets no row, so no Qayıb and no deduction on anyone's payroll.
        using var h = new Harness();
        h.MarkAbsentYesterday(h.WorkerId);
        h.MarkAbsentYesterday(h.PanelManagerId);

        await new DailySummaryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" }).GenerateForDateAsync(h.Yesterday);
        h.Db.ChangeTracker.Clear();
        var written = await h.Db.DailySummaries.AsNoTracking()
            .Where(s => s.SummaryDate == h.Yesterday).Select(s => s.EmployeeId).ToListAsync();

        Assert.Contains(h.WorkerId, written);
        Assert.DoesNotContain(h.PanelManagerId, written);
    }

    [Fact]
    public void The_flag_is_off_unless_someone_sets_it()
    {
        Assert.False(new Employee().PanelOnly);
    }
}
