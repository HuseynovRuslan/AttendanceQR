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
/// A day spent on FIELD visits must count as worked in DailySummary — the table the monthly tabel and
/// the payroll deduction both read. Until 2026-08-09 field visits reached only the live "today" board,
/// so a worker sent to poster-less sites all month read as Qayıb every day and the payroll deducted a
/// month's pay from someone who had worked. These pin that fix, plus the boundaries around it: an
/// office scan still wins, an assigned-but-never-visited day is still an absence, and a field day
/// never invents lateness.
/// </summary>
public class FieldVisitSummaryTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public DailySummaryService Service { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        /// <summary>Yesterday, local — GenerateForDateAsync refuses today and any future date.</summary>
        public DateOnly Day { get; }

        public Harness(int workDaysMask = 127)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fv-sum-{Guid.NewGuid()}")
                .Options;
            Db = new AppDbContext(options, tenant);
            Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku)).AddDays(-1);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Baş ofis",
                Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = workDaysMask, QrVersion = 1, IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Sahə İşçisi", LocationId = LocationId,
                Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
                CanFieldCheckIn = true,
                // Activated well before the day under test, or the summary skips them as not-yet-onboarded.
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            Db.SaveChanges();

            Service = new DailySummaryService(Db, new AppOptions { TimeZone = "Asia/Baku" });
        }

        /// <summary>A UTC instant at the given LOCAL hour on the day under test.</summary>
        public DateTime At(int localHour, int localMinute = 0)
        {
            var local = Day.ToDateTime(new TimeOnly(localHour, localMinute));
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Baku);
        }

        public void AddVisit(DateTime? checkIn, DateTime? checkOut, FieldVisitStatus status)
        {
            Db.FieldVisits.Add(new FieldVisit
            {
                TenantId = TenantId, EmployeeId = EmployeeId, VisitDate = Day, Status = status,
                CheckInAtUtc = checkIn, CheckOutAtUtc = checkOut,
            });
            Db.SaveChanges();
        }

        public void AddOfficeRecord(DateTime checkIn, DateTime? checkOut)
        {
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LocationId = LocationId,
                AttendanceDate = Day, CheckInAtUtc = checkIn, CheckOutAtUtc = checkOut,
            });
            Db.SaveChanges();
        }

        public async Task<DailySummary> RunAsync()
        {
            await Service.GenerateForDateAsync(Day);
            Db.ChangeTracker.Clear();
            return Db.DailySummaries.AsNoTracking().Single(s => s.EmployeeId == EmployeeId && s.SummaryDate == Day);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Completed_field_visit_counts_as_worked_not_absent()
    {
        using var h = new Harness();
        h.AddVisit(h.At(9), h.At(17), FieldVisitStatus.Completed);

        var s = await h.RunAsync();

        Assert.Equal(DailySummaryStatus.OnTime, s.Status);
        Assert.Equal(8 * 60, s.WorkedMinutes);
        Assert.Equal(h.At(9), s.CheckInAtUtc);
        Assert.Equal(h.At(17), s.CheckOutAtUtc);
    }

    [Fact]
    public async Task Several_visits_span_from_first_arrival_to_last_departure()
    {
        using var h = new Harness();
        h.AddVisit(h.At(9), h.At(11), FieldVisitStatus.Completed);
        h.AddVisit(h.At(13), h.At(16), FieldVisitStatus.Completed);

        var s = await h.RunAsync();

        Assert.Equal(DailySummaryStatus.OnTime, s.Status);
        Assert.Equal(7 * 60, s.WorkedMinutes); // 09:00 → 16:00, travel between sites included
    }

    [Fact]
    public async Task An_open_visit_leaves_the_day_incomplete()
    {
        using var h = new Harness();
        h.AddVisit(h.At(9), h.At(11), FieldVisitStatus.Completed);
        h.AddVisit(h.At(14), null, FieldVisitStatus.CheckedIn); // forgot to check out

        var s = await h.RunAsync();

        // Same as a forgotten office check-out — worked, but flagged for /admin/field-visits to close.
        Assert.Equal(DailySummaryStatus.Incomplete, s.Status);
        Assert.Null(s.CheckOutAtUtc);
    }

    [Fact]
    public async Task An_office_scan_wins_over_a_field_visit()
    {
        using var h = new Harness();
        h.AddOfficeRecord(h.At(9), h.At(18));
        h.AddVisit(h.At(12), h.At(13), FieldVisitStatus.Completed);

        var s = await h.RunAsync();

        // The scan decides the day; field minutes are NOT added on top (they would double-count).
        Assert.Equal(9 * 60, s.WorkedMinutes);
        Assert.Equal(h.At(18), s.CheckOutAtUtc);
    }

    [Theory]
    [InlineData(FieldVisitStatus.Assigned)]  // sent, never went
    [InlineData(FieldVisitStatus.Cancelled)] // called off
    public async Task A_visit_without_a_real_arrival_is_still_an_absence(FieldVisitStatus status)
    {
        using var h = new Harness();
        h.AddVisit(status == FieldVisitStatus.Assigned ? null : h.At(9),
                   null, status);

        var s = await h.RunAsync();

        Assert.Equal(DailySummaryStatus.Absent, s.Status);
        Assert.Equal(0, s.WorkedMinutes);
    }

    [Fact]
    public async Task Field_work_on_a_non_working_day_still_counts_as_worked()
    {
        // WorkDaysMask 0 = no scheduled work days at all, so the day would otherwise be DayOff.
        using var h = new Harness(workDaysMask: 0);
        h.AddVisit(h.At(10), h.At(14), FieldVisitStatus.Completed);

        var s = await h.RunAsync();

        Assert.Equal(DailySummaryStatus.OnTime, s.Status);
        Assert.Equal(4 * 60, s.WorkedMinutes);
    }

    [Fact]
    public async Task Field_work_during_approved_leave_still_counts_as_worked()
    {
        using var h = new Harness();
        h.Db.LeaveRecords.Add(new LeaveRecord
        {
            TenantId = TenantId, EmployeeId = h.EmployeeId, FromDate = h.Day, ToDate = h.Day,
            Type = LeaveType.Vacation, CreatedByEmployeeId = h.EmployeeId,
        });
        h.Db.SaveChanges();
        h.AddVisit(h.At(9), h.At(15), FieldVisitStatus.Completed);

        var s = await h.RunAsync();

        // Turning up is worked time — the same rule a scan on a leave day already follows.
        Assert.Equal(DailySummaryStatus.OnTime, s.Status);
        Assert.Equal(6 * 60, s.WorkedMinutes);
    }

    [Fact]
    public async Task A_late_field_arrival_is_never_recorded_as_lateness()
    {
        using var h = new Harness();
        // Shift starts 09:00; the manager sent them to a site they reached at 11:30.
        h.AddVisit(h.At(11, 30), h.At(17), FieldVisitStatus.Completed);

        var s = await h.RunAsync();

        Assert.Equal(DailySummaryStatus.OnTime, s.Status);
        Assert.Equal(0, s.LateMinutes);
    }

    [Fact]
    public async Task A_plain_day_with_no_attendance_at_all_is_still_absent()
    {
        // Guard against the change turning every empty day into worked time.
        using var h = new Harness();

        var s = await h.RunAsync();

        Assert.Equal(DailySummaryStatus.Absent, s.Status);
        Assert.Equal(0, s.WorkedMinutes);
    }

    // --- the LIVE path (today) ---------------------------------------------------
    // Today is never read from DailySummary — it is computed on demand in ReportQueryService. That is
    // a second implementation of the same rule, so it gets the same coverage: otherwise a field day
    // would count as worked all month and flip to Qayıb for the current day only.

    [Fact]
    public async Task Live_today_counts_a_field_day_as_worked_and_labels_it_Sahede()
    {
        using var h = new Harness();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku));
        h.Db.FieldVisits.Add(new FieldVisit
        {
            TenantId = TenantId, EmployeeId = h.EmployeeId, VisitDate = today,
            Status = FieldVisitStatus.Completed,
            CheckInAtUtc = DateTime.UtcNow.AddHours(-3), CheckOutAtUtc = DateTime.UtcNow.AddHours(-1),
        });
        h.Db.SaveChanges();

        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" });
        var board = await reports.GetTodayAttendanceAsync(h.EmployeeId, EmployeeRole.Admin);

        var row = Assert.Single(board);
        Assert.Equal("Field", row.Status);            // board badge stays "Sahədə"
        Assert.NotNull(row.FieldCheckInAtUtc);
        Assert.Null(row.CheckInAtUtc);                // no office scan — the field day is not one

        // And the same day, as the reports/tabel see it: worked, ~120 minutes, not an absence.
        var (_, report) = await reports.GetSummaryAsync(today, today, null, h.EmployeeId, EmployeeRole.Admin);
        var me = Assert.Single(report!.Rows);
        Assert.Equal(0, me.AbsentDays);
        Assert.Equal(1, me.WorkDays);
        Assert.InRange(me.TotalWorkedHours, 1.9, 2.1);
    }

    [Fact]
    public async Task Live_today_leaves_a_plain_absent_day_alone()
    {
        using var h = new Harness();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku));

        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" });
        var (_, report) = await reports.GetSummaryAsync(today, today, null, h.EmployeeId, EmployeeRole.Admin);

        var me = Assert.Single(report!.Rows);
        Assert.Equal(0, me.WorkDays);
    }
}
