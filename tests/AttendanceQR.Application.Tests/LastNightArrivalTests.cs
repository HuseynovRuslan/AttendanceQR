using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// An arrival made after midnight for last night's shift is filed under last night's date (owner's
/// decision, 2026-11-14) — in the FIRST HALF of that shift only (owner's revision, 2026-11-16).
/// 23:50–08:00 with fifteen minutes of grace:
///   23:55 → that day, on time · 00:03 → the day before, on time · 00:06 → the day before, late ·
///   03:54 → the day before, late · 03:55, the second half → today, the ordinary rules.
/// The timestamp is never moved, the audit trail is the ordinary one, one arrival opens one day, and
/// day shifts and the ordinary after-midnight scans are untouched. Every case goes through the real
/// Scan endpoint with the clock pinned.
/// </summary>
public class LastNightArrivalTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000a4a4");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private const double Lat = 40.4093;
    private const double Lng = 49.8671;

    /// <summary>Monday 5 October 2026 — «last night» in these tests. D1 is the Tuesday after.</summary>
    private static readonly DateOnly D0 = new(2026, 10, 5);
    private static readonly DateOnly D1 = new(2026, 10, 6);

    private static DateTime Utc(DateOnly date, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Baku);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class SpyRebuild : ISummaryRebuildQueue
    {
        public List<DateOnly> Asked { get; } = [];
        public void Request(Guid tenantId, DateOnly date) => Asked.Add(date);
        public IReadOnlyList<(Guid TenantId, DateOnly Date)> TakeSettled(TimeSpan settle, DateTime nowUtc) => [];
    }

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public SpyRebuild Rebuild { get; } = new();
        private readonly IQrTokenService _qr = new QrTokenService(Options.Create(new QrTokenOptions
        {
            Secret = "test-secret-key-for-last-night-arrival-tests", TtlSeconds = 300,
        }));

        /// <param name="secondWindow">A split day: the person gets a named shift with these hours as its
        /// first window and this as its second. Null — the branch's own hours, as every other test.</param>
        public Harness(TimeOnly start, TimeOnly end, int workDaysMask = 127, bool qrless = false,
            (TimeOnly Start, TimeOnly End)? secondWindow = null)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"last-night-{Guid.NewGuid()}")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Anbar", Latitude = Lat, Longitude = Lng, RadiusMeters = 150,
                ShiftStart = start, ShiftEnd = end, LateThresholdMinutes = 15, WorkDaysMask = workDaysMask,
                QrVersion = 1, IsActive = true, QrlessCheckIn = qrless,
            });
            Guid? scheduleId = null;
            if (secondWindow is { } second)
            {
                scheduleId = Guid.NewGuid();
                Db.Schedules.Add(new Schedule
                {
                    Id = scheduleId.Value, TenantId = TenantId, LocationId = LocationId, Name = "İkiqat gün",
                    ShiftStart = start, ShiftEnd = end, SecondShiftStart = second.Start, SecondShiftEnd = second.End,
                    LateThresholdMinutes = 15, WorkDaysMask = workDaysMask,
                });
            }
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Gece Isci", Email = "gece@t.local",
                LocationId = LocationId, ScheduleId = scheduleId, Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
                MonthlySalary = 3000m, ActivatedAtUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            });
            Db.SaveChanges();
        }

        /// <summary>A second person at the same branch, on the same hours — for one phone, two accounts.</summary>
        public Guid AddColleague(bool canShareDevice)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = "Gece Isci 2", Email = "gece2@t.local",
                LocationId = LocationId, Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
                CanShareDevice = canShareDevice, ActivatedAtUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            });
            var self = Db.Employees.Single(e => e.Id == EmployeeId);
            self.CanShareDevice = canShareDevice;
            Db.SaveChanges();
            return id;
        }

        public AttendanceController At(DateTime serverUtc) => As(EmployeeId, serverUtc);

        public AttendanceController As(Guid employeeId, DateTime serverUtc) =>
            new(Db, _qr, new AttendanceQueryService(Db), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(),
                new StubFace(), new DeviceBindingOptions { AutoBind = true }, new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()), NullLogger<AttendanceController>.Instance,
                summaryRebuild: Rebuild, clock: new FixedClock(serverUtc))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", employeeId.ToString())], "test")),
                    },
                },
            };

        public ScanRequest Poster(DateTime? offlineAtUtc = null) =>
            new(_qr.Generate(LocationId, 1), "device-fp-1", Lat, Lng,
                ClientTimestampUtc: offlineAtUtc, Offline: offlineAtUtc is not null);

        public ScanRequest NoPoster() => new(null, "device-fp-1", Lat, Lng);

        public void Seed(DateOnly date, DateTime inUtc, DateTime? outUtc)
        {
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LocationId = LocationId,
                AttendanceDate = date, CheckInAtUtc = inUtc, CheckOutAtUtc = outUtc, Status = AttendanceStatus.OnTime,
            });
            Db.SaveChanges();
        }

        public Task<List<AttendanceRecord>> RecordsAsync() => RecordsOfAsync(EmployeeId);

        public Task<List<AttendanceRecord>> RecordsOfAsync(Guid employeeId) =>
            Db.AttendanceRecords.AsNoTracking().Where(r => r.EmployeeId == employeeId)
                .OrderBy(r => r.CheckInAtUtc).ToListAsync();

        public void Dispose() => Db.Dispose();
    }

    private static string? Action(IActionResult r) =>
        (r as OkObjectResult)?.Value?.GetType().GetProperty("action")?.GetValue(((OkObjectResult)r).Value) as string;

    private static readonly TimeOnly NightStart = new(23, 50);
    private static readonly TimeOnly NightEnd = new(8, 0);

    // ─── the owner's examples ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 23, 55, 0, AttendanceStatus.OnTime)]  // before midnight: that day, inside the grace
    [InlineData(1, 0, 3, 0, AttendanceStatus.OnTime)]    // after midnight: last night, inside the grace
    [InlineData(1, 0, 5, 0, AttendanceStatus.OnTime)]    // the last minute of the grace
    [InlineData(1, 0, 6, 0, AttendanceStatus.Late)]      // last night, late
    [InlineData(1, 3, 54, 0, AttendanceStatus.Late)]     // the last minute of the first half
    [InlineData(1, 3, 55, 1, AttendanceStatus.OnTime)]   // the midpoint: the second half files today
    [InlineData(1, 7, 59, 1, AttendanceStatus.OnTime)]   // the shift still running, but not its arrival
    public async Task An_arrival_for_last_nights_shift_is_filed_under_that_night(
        int scanDay, int hour, int minute, int filedDay, AttendanceStatus expected)
    {
        using var h = new Harness(NightStart, NightEnd);
        var at = Utc(D0.AddDays(scanDay), hour, minute);

        var result = await h.At(at).Scan(h.Poster());

        Assert.Equal("CheckIn", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D0.AddDays(filedDay), record.AttendanceDate);
        Assert.Equal(at, record.CheckInAtUtc);          // the real instant, never moved
        Assert.Equal(expected, record.Status);
    }

    [Fact]
    public async Task Once_the_shift_is_over_the_ordinary_rules_decide()
    {
        using var h = new Harness(NightStart, NightEnd);

        await h.At(Utc(D1, 8, 0)).Scan(h.Poster());

        Assert.Equal(D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task The_morning_exit_closes_the_night_the_late_arrival_opened()
    {
        using var h = new Harness(NightStart, NightEnd);
        await h.At(Utc(D1, 0, 6)).Scan(h.Poster());

        var exit = await h.At(Utc(D1, 7, 40)).Scan(h.Poster());

        Assert.Equal("CheckOut", Action(exit));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D0, record.AttendanceDate);
        Assert.Equal(Utc(D1, 0, 6), record.CheckInAtUtc);
        Assert.Equal(Utc(D1, 7, 40), record.CheckOutAtUtc);
    }

    // ─── one arrival, one day ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_night_that_already_has_a_record_is_never_given_a_second_one()
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Seed(D0, Utc(D0, 23, 51), Utc(D0, 23, 58)); // came and left before midnight

        await h.At(Utc(D1, 0, 30)).Scan(h.Poster());

        var records = await h.RecordsAsync();
        Assert.Single(records, r => r.AttendanceDate == D0);   // untouched
        Assert.Single(records, r => r.AttendanceDate == D1);   // the ordinary rule, as before
    }

    [Fact]
    public async Task An_open_night_is_still_closed_by_the_scan_but_asks_first_when_it_is_soon()
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Seed(D0, Utc(D0, 23, 52), null);

        // 38 minutes after arriving: the same «are you leaving?» the day path asks within two hours.
        var soon = await h.At(Utc(D1, 0, 30)).Scan(h.Poster());
        Assert.IsType<ConflictObjectResult>(soon);
        Assert.Null(Assert.Single(await h.RecordsAsync()).CheckOutAtUtc);

        var confirmed = await h.At(Utc(D1, 0, 31)).Scan(h.Poster() with { ConfirmEarlyCheckOut = true });
        Assert.Equal("CheckOut", Action(confirmed));
        Assert.Equal(D0, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    // ─── what must not notice any of this ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(8, 0, 18, 0)]    // a day shift
    [InlineData(18, 0, 23, 30)]  // an evening shift that ends before midnight
    [InlineData(0, 0, 8, 0)]     // a shift that STARTS at midnight belongs to the new day
    public async Task Shifts_that_do_not_cross_midnight_file_an_after_midnight_scan_under_the_new_day(
        int sh, int sm, int eh, int em)
    {
        using var h = new Harness(new TimeOnly(sh, sm), new TimeOnly(eh, em));

        await h.At(Utc(D1, 0, 10)).Scan(h.Poster());

        Assert.Equal(D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task Last_night_being_a_day_off_keeps_the_scan_on_the_new_day()
    {
        // Monday off (bit 1 of the Sunday-first mask cleared): there was no shift last night to arrive for.
        using var h = new Harness(NightStart, NightEnd, workDaysMask: 127 & ~(1 << (int)DayOfWeek.Monday));

        await h.At(Utc(D1, 0, 3)).Scan(h.Poster());

        Assert.Equal(D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    // ─── every way in ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_offline_scan_is_judged_by_when_it_was_taken_and_the_night_is_rebuilt()
    {
        using var h = new Harness(NightStart, NightEnd);
        var taken = Utc(D1, 0, 4);

        // Synced at 09:30, after the shift and after the 00:30 job: the phone's own time decides.
        var result = await h.At(Utc(D1, 9, 30)).Scan(h.Poster(offlineAtUtc: taken));

        Assert.Equal("CheckIn", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D0, record.AttendanceDate);
        Assert.Equal(taken, record.CheckInAtUtc);
        Assert.True(record.WasOffline);
        Assert.Equal(AttendanceStatus.OnTime, record.Status);
        Assert.Contains(D0, h.Rebuild.Asked);          // the stored night is recomputed
    }

    [Fact]
    public async Task A_branch_without_a_poster_follows_the_same_rule()
    {
        using var h = new Harness(NightStart, NightEnd, qrless: true);

        var result = await h.At(Utc(D1, 0, 6)).Scan(h.NoPoster());

        Assert.Equal("CheckIn", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D0, record.AttendanceDate);
        Assert.Equal(AttendanceStatus.Late, record.Status);
    }

    [Fact]
    public async Task The_audit_trail_is_the_ordinary_one_check_in()
    {
        using var h = new Harness(NightStart, NightEnd);
        var at = Utc(D1, 0, 3);

        await h.At(at).Scan(h.Poster());

        // Only the attendance events: the first scan from a new phone also writes its device adoption.
        var attendance = await h.Db.AuditLogs.AsNoTracking()
            .Where(a => a.EmployeeId == h.EmployeeId
                        && (a.EventType == AuditEventType.CheckInSuccess || a.EventType == AuditEventType.CheckInRejected
                            || a.EventType == AuditEventType.CheckOutSuccess || a.EventType == AuditEventType.CheckOutRejected))
            .ToListAsync();
        var only = Assert.Single(attendance);
        Assert.Equal(AuditEventType.CheckInSuccess, only.EventType);
        Assert.Null(only.Reason); // the ordinary check-in line, nothing new written about it
    }

    // ─── the night job, the tabel, the payroll, the Excel file, the dashboard ────────────────────────

    [Fact]
    public async Task After_the_night_job_every_report_says_the_night_was_worked_and_late()
    {
        using var h = new Harness(NightStart, NightEnd);
        await h.At(Utc(D1, 0, 6)).Scan(h.Poster());
        await h.At(Utc(D1, 7, 40)).Scan(h.Poster());

        var options = new AppOptions { TimeZone = "Asia/Baku" };
        await new DailySummaryService(h.Db, options).GenerateForDateAsync(D0);
        var stored = Assert.Single(h.Db.DailySummaries.AsNoTracking().Where(s => s.SummaryDate == D0));
        Assert.Equal(DailySummaryStatus.Late, stored.Status);
        Assert.Equal(16, stored.LateMinutes);

        var reports = new ReportQueryService(h.Db, options, new FixedClock(Utc(D1, 12, 0)));
        var (_, tabel) = await reports.GetTabelAsync(D0.Year, D0.Month, null, h.EmployeeId, EmployeeRole.Admin);
        var row = Assert.Single(tabel!.Rows);
        Assert.Equal("İ", row.Days[D0.Day - 1]);
        Assert.Equal("G", row.Days[D1.Day - 1]);     // tonight's shift has not started at noon

        var (_, summary) = await reports.GetSummaryAsync(D0, D0, null, h.EmployeeId, EmployeeRole.Admin);
        var s = Assert.Single(summary!.Rows);
        Assert.Equal((1, 0, 1), (s.WorkDays, s.AbsentDays, s.LateCount));

        var (_, pay) = await reports.GetPayrollAsync(D0, D0, null, h.EmployeeId, EmployeeRole.Admin);
        Assert.Equal(0m, Assert.Single(pay!.Rows).Deduction);

        var (_, dash) = await reports.GetDashboardAsync(D0, D0, null, h.EmployeeId, EmployeeRole.Admin);
        Assert.Equal((0, 1), (dash!.AbsentCount, dash.LateCount));

        using var book = new XLWorkbook(new MemoryStream(new ExcelReportExporter().BuildTabel(tabel)));
        var sheet = book.Worksheets.First();
        Assert.Equal("İ", sheet.Cell(3, 2 + D0.Day).GetString());
    }

    // ─── the rule and the lateness, on their own ─────────────────────────────────────────────────────

    [Theory]
    //          now     scheduled start   end   todayScheduled todayStart  expected
    [InlineData("00:03", true, "23:50", "08:00", true, "23:50", true)]   // consecutive nights
    [InlineData("03:54", true, "23:50", "08:00", true, "23:50", true)]   // owner's example: until 03:55
    [InlineData("03:55", true, "23:50", "08:00", true, "23:50", false)]  // …from it on, the second half
    [InlineData("07:59", true, "23:50", "08:00", true, "23:50", false)]
    [InlineData("00:00", true, "18:00", "08:00", true, "18:00", true)]   // owner's example: until 01:00
    [InlineData("00:59", true, "18:00", "08:00", true, "18:00", true)]
    [InlineData("01:00", true, "18:00", "08:00", true, "18:00", false)]
    [InlineData("07:46", true, "18:00", "08:00", true, "18:00", false)]  // the guard of 15.11
    [InlineData("01:59", true, "22:00", "06:00", true, "22:00", true)]   // owner's example: until 02:00
    [InlineData("02:00", true, "22:00", "06:00", true, "22:00", false)]
    [InlineData("00:24", true, "23:50", "01:00", true, "23:50", true)]   // a 70-minute night: until 00:25
    [InlineData("00:25", true, "23:50", "01:00", true, "23:50", false)]
    [InlineData("00:10", true, "22:00", "01:00", true, "22:00", false)]  // midpoint before midnight: no first half left
    [InlineData("00:01", true, "20:00", "04:00", true, "20:00", false)]  // midpoint exactly at midnight
    [InlineData("00:03", true, "23:50", "08:00", true, "14:00", true)]   // today starts in the afternoon
    [InlineData("00:03", true, "23:50", "08:00", false, "23:50", true)]  // today is a day off
    [InlineData("08:00", true, "23:50", "08:00", true, "23:50", false)]  // the shift is over
    [InlineData("12:30", true, "23:50", "13:00", true, "23:50", false)]  // afternoon: never
    [InlineData("00:03", false, "23:50", "08:00", true, "23:50", false)] // last night was not scheduled
    [InlineData("00:03", true, "08:00", "18:00", true, "08:00", false)]  // not overnight
    [InlineData("05:30", true, "22:00", "08:00", true, "08:00", false)]  // today has a MORNING shift: today's
    [InlineData("02:00", true, "22:00", "08:00", true, "06:00", false)]  // …at any hour before noon
    [InlineData("07:45", true, "22:00", "08:00", true, "00:00", false)]  // a shift starting at midnight is a morning one
    public void The_timetable_half_of_the_rule(string now, bool scheduled, string start, string end,
        bool todayScheduled, string todayStart, bool expected)
        => Assert.Equal(expected, NightScanRules.MayBeArrivalForLastNightsShift(
            TimeOnly.Parse(now), scheduled, TimeOnly.Parse(start), TimeOnly.Parse(end),
            todayScheduled, TimeOnly.Parse(todayStart)));

    // ─── review findings: a night that is not free, a morning crew, the «did it work?» tap ───────────

    [Theory]
    [InlineData(LeaveType.Sick)]
    [InlineData(LeaveType.Vacation)]
    [InlineData(LeaveType.Permission)]
    public async Task A_night_covered_by_leave_is_never_turned_into_a_worked_night(LeaveType type)
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Db.LeaveRecords.Add(new LeaveRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = h.EmployeeId, FromDate = D0, ToDate = D0,
            Type = type, CreatedByEmployeeId = h.EmployeeId,
        });
        h.Db.SaveChanges();

        await h.At(Utc(D1, 0, 3)).Scan(h.Poster());

        Assert.Equal(D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task A_holiday_night_keeps_the_scan_on_the_new_day()
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Db.NonWorkingDays.Add(new NonWorkingDay { TenantId = TenantId, Date = D0, Description = "Bayram" });
        h.Db.SaveChanges();

        await h.At(Utc(D1, 0, 3)).Scan(h.Poster());

        Assert.Equal(D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Theory]
    [InlineData(true, false)]   // a holiday declared for THIS person's branch: the night was not a working one
    [InlineData(false, true)]   // another branch's holiday says nothing about this night
    public async Task A_branch_holiday_counts_only_for_its_own_branch(bool ownBranch, bool filedUnderNight)
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Db.NonWorkingDays.Add(new NonWorkingDay
        {
            TenantId = TenantId, Date = D0, Description = "Filial bayramı", LocationId = ownBranch ? h.LocationId : Guid.NewGuid(),
        });
        h.Db.SaveChanges();

        await h.At(Utc(D1, 0, 3)).Scan(h.Poster());

        Assert.Equal(filedUnderNight ? D0 : D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task A_night_with_a_field_visit_keeps_the_scan_on_the_new_day()
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Db.FieldVisits.Add(new FieldVisit
        {
            TenantId = TenantId, EmployeeId = h.EmployeeId, VisitDate = D0, Status = FieldVisitStatus.Completed,
            CheckInAtUtc = Utc(D0, 23, 50), CheckOutAtUtc = Utc(D1, 0, 1),
        });
        h.Db.SaveChanges();

        await h.At(Utc(D1, 0, 3)).Scan(h.Poster());

        Assert.Single(await h.RecordsAsync(), r => r.AttendanceDate == D1);
    }

    [Theory]
    [InlineData(7, 45)]  // early for this morning's 08:00 start
    [InlineData(3, 0)]   // or hours before it: with a morning shift today, a morning scan is today's
    public async Task A_morning_crew_after_a_night_shift_files_under_today(int hour, int minute)
    {
        using var h = new Harness(NightStart, NightEnd);
        // The plan puts this person on 08:00–15:00 today, after last night's 23:50–08:00.
        var morning = Guid.NewGuid();
        h.Db.Schedules.Add(new Schedule
        {
            Id = morning, TenantId = TenantId, Name = "Səhər", ShiftStart = new TimeOnly(8, 0),
            ShiftEnd = new TimeOnly(15, 0), LateThresholdMinutes = 15, WorkDaysMask = 127,
        });
        h.Db.ShiftOverrides.Add(new ShiftOverride
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = h.EmployeeId, Date = D1, ScheduleId = morning,
            CreatedAtUtc = DateTime.UtcNow,
        });
        h.Db.SaveChanges();

        await h.At(Utc(D1, hour, minute)).Scan(h.Poster());

        Assert.Equal(D1, Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task A_did_it_work_tap_after_a_night_arrival_asks_before_closing_the_night()
    {
        using var h = new Harness(NightStart, NightEnd);
        await h.At(Utc(D1, 0, 3)).Scan(h.Poster());

        // Seventeen minutes later — not a leaving, a «did it work?».
        var tap = await h.At(Utc(D1, 0, 20)).Scan(h.Poster());
        var conflict = Assert.IsType<ConflictObjectResult>(tap);
        Assert.Equal("ConfirmEarlyCheckOut", conflict.Value!.GetType().GetProperty("error")!.GetValue(conflict.Value));
        Assert.Null(Assert.Single(await h.RecordsAsync()).CheckOutAtUtc);   // nothing written

        // Said yes: then it is a real early exit, and it closes the night it belongs to.
        var yes = await h.At(Utc(D1, 0, 25)).Scan(h.Poster() with { ConfirmEarlyCheckOut = true });
        Assert.Equal("CheckOut", Action(yes));
        var night = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D0, night.AttendanceDate);
        Assert.Equal(Utc(D1, 0, 25), night.CheckOutAtUtc);
    }

    [Fact]
    public async Task An_ordinary_night_exit_hours_later_needs_no_confirmation()
    {
        using var h = new Harness(NightStart, NightEnd);
        h.Seed(D0, Utc(D0, 23, 48), null);

        var result = await h.At(Utc(D1, 7, 55)).Scan(h.Poster());

        Assert.Equal("CheckOut", Action(result));
    }

    [Theory]
    [InlineData(0, 23, 55, AttendanceStatus.OnTime)]  // the old time-of-day test wrapped 23:50+15 to 00:05 and said Late
    [InlineData(1, 0, 5, AttendanceStatus.OnTime)]
    [InlineData(1, 0, 6, AttendanceStatus.Late)]
    public void Lateness_is_measured_on_the_calendar(int day, int hour, int minute, AttendanceStatus expected)
        => Assert.Equal(expected,
            AttendanceController.DetermineStatus(D0, NightStart, 15, Utc(D0.AddDays(day), hour, minute), Baku));

    [Theory]
    [InlineData(9, 15, AttendanceStatus.OnTime)]
    [InlineData(9, 16, AttendanceStatus.Late)]
    [InlineData(7, 30, AttendanceStatus.OnTime)]
    public void A_day_shift_is_judged_exactly_as_before(int hour, int minute, AttendanceStatus expected)
    {
        var at = Utc(D0, hour, minute);
        var start = new TimeOnly(9, 0);
        Assert.Equal(expected, AttendanceController.DetermineStatus(D0, start, 15, at, Baku));
    }

    // ─── the first half only (owner's revision, 2026-11-16) ──────────────────────────────────────────

    private static readonly DateOnly D2 = new(2026, 10, 7);

    [Theory]
    //          start   end     scan   under last night?
    [InlineData(18, 0, 8, 0, 0, 0, true)]     // midnight
    [InlineData(18, 0, 8, 0, 0, 59, true)]
    [InlineData(18, 0, 8, 0, 1, 0, false)]    // owner: 18:00–08:00 → until 01:00
    [InlineData(18, 0, 8, 0, 4, 30, false)]   // the middle of the second half
    [InlineData(18, 0, 8, 0, 7, 46, false)]   // the guard of 15.11
    [InlineData(22, 0, 6, 0, 1, 59, true)]
    [InlineData(22, 0, 6, 0, 2, 0, false)]    // owner: 22:00–06:00 → until 02:00
    [InlineData(23, 50, 8, 0, 3, 54, true)]
    [InlineData(23, 50, 8, 0, 3, 55, false)]  // owner: 23:50–08:00 → until 03:55
    public async Task Only_the_first_half_of_a_night_can_be_its_arrival(
        int sh, int sm, int eh, int em, int hour, int minute, bool lastNight)
    {
        using var h = new Harness(new TimeOnly(sh, sm), new TimeOnly(eh, em));
        var at = Utc(D1, hour, minute);

        var result = await h.At(at).Scan(h.Poster());

        Assert.Equal("CheckIn", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(lastNight ? D0 : D1, record.AttendanceDate);
        Assert.Equal(at, record.CheckInAtUtc);                    // the real instant, never moved
        // Lateness is its own question, measured on the day the record is filed under: every first-half
        // arrival above is past last night's start + 15; a second-half scan is hours before tonight's.
        Assert.Equal(lastNight ? AttendanceStatus.Late : AttendanceStatus.OnTime, record.Status);
    }

    [Theory]
    [InlineData("18:00", "08:00", "01:00")]
    [InlineData("22:00", "06:00", "02:00")]
    [InlineData("23:50", "08:00", "03:55")]
    [InlineData("23:50", "08:01", "03:55:30")]
    [InlineData("22:00", "01:00", null)]   // midpoint 23:30 — nothing of the first half is after midnight
    [InlineData("20:00", "04:00", null)]   // midpoint exactly at midnight
    [InlineData("08:00", "18:00", null)]   // not a night
    public void The_first_half_ends_at_the_midpoint(string start, string end, string? expected)
    {
        TimeOnly? want = expected is null ? null : TimeOnly.Parse(expected);
        Assert.Equal(want, NightScanRules.FirstHalfEndsAfterMidnight(TimeOnly.Parse(start), TimeOnly.Parse(end)));
    }

    [Fact]
    public async Task The_guards_morning_is_a_days_work_and_the_night_he_missed_stays_absent()
    {
        // Production, 15.11: 18:00–08:00, no scan the evening before, in at 07:46, out at 17:51, nothing
        // the next morning. Filed under the night, a night he was not there for became a worked day and
        // the ten hours he did work read as zero. In the second half the scan files today, as it always did.
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));

        Assert.Equal("CheckIn", Action(await h.At(Utc(D1, 7, 46)).Scan(h.Poster())));
        Assert.Equal("CheckOut", Action(await h.At(Utc(D1, 17, 51)).Scan(h.Poster())));

        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D1, record.AttendanceDate);
        Assert.Equal(Utc(D1, 7, 46), record.CheckInAtUtc);
        Assert.Equal(Utc(D1, 17, 51), record.CheckOutAtUtc);

        var options = new AppOptions { TimeZone = "Asia/Baku" };
        await new DailySummaryService(h.Db, options).GenerateForDateAsync(D0);
        await new DailySummaryService(h.Db, options).GenerateForDateAsync(D1);
        Assert.Equal(DailySummaryStatus.Absent,
            h.Db.DailySummaries.AsNoTracking().Single(s => s.SummaryDate == D0).Status);
        Assert.NotEqual(DailySummaryStatus.Absent,
            h.Db.DailySummaries.AsNoTracking().Single(s => s.SummaryDate == D1).Status);

        var reports = new ReportQueryService(h.Db, options, new FixedClock(Utc(D2, 12, 0)));
        var (_, tabel) = await reports.GetTabelAsync(D0.Year, D0.Month, null, h.EmployeeId, EmployeeRole.Admin);
        var row = Assert.Single(tabel!.Rows);
        Assert.Equal(("Q", "İ"), (row.Days[D0.Day - 1], row.Days[D1.Day - 1]));

        var (_, summary) = await reports.GetSummaryAsync(D0, D1, null, h.EmployeeId, EmployeeRole.Admin);
        var s = Assert.Single(summary!.Rows);
        Assert.Equal((1, 1), (s.WorkDays, s.AbsentDays));

        var (_, pay) = await reports.GetPayrollAsync(D0, D1, null, h.EmployeeId, EmployeeRole.Admin);
        var p = Assert.Single(pay!.Rows);
        Assert.Equal((1, p.PerDay), (p.AbsentDays, p.Deduction));   // the night he missed, and only it

        var (_, dash) = await reports.GetDashboardAsync(D0, D1, null, h.EmployeeId, EmployeeRole.Admin);
        Assert.Equal(1, dash!.AbsentCount);

        using var book = new XLWorkbook(new MemoryStream(new ExcelReportExporter().BuildTabel(tabel)));
        var sheet = book.Worksheets.First();
        Assert.Equal(("Q", "İ"), (sheet.Cell(3, 2 + D0.Day).GetString(), sheet.Cell(3, 2 + D1.Day).GetString()));
    }

    [Fact]
    public async Task A_real_arrival_in_the_second_half_files_today_and_tonights_arrival_is_not_refused()
    {
        // Somebody truly eight and a half hours late: 02:30 for 18:00–08:00. The second half files today;
        // the morning exit closes it; tonight's arrival must still open tonight — refusing it as
        // «artıq tamamlamısınız» would leave the night unrecorded and start the morning-stray cascade.
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));

        Assert.Equal("CheckIn", Action(await h.At(Utc(D1, 2, 30)).Scan(h.Poster())));
        Assert.Equal("CheckOut", Action(await h.At(Utc(D1, 8, 0)).Scan(h.Poster())));
        Assert.Equal("CheckIn", Action(await h.At(Utc(D1, 17, 55)).Scan(h.Poster())));
        Assert.Equal("CheckOut", Action(await h.At(Utc(D2, 8, 0)).Scan(h.Poster())));
        // A later tap that morning repeats the exit; it must not open a stray day.
        Assert.IsType<ConflictObjectResult>(await h.At(Utc(D2, 8, 20)).Scan(h.Poster()));

        var records = await h.RecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(D1, r.AttendanceDate));
        Assert.Equal((Utc(D1, 2, 30), Utc(D1, 8, 0)), (records[0].CheckInAtUtc!.Value, records[0].CheckOutAtUtc!.Value));
        Assert.Equal((Utc(D1, 17, 55), Utc(D2, 8, 0)), (records[1].CheckInAtUtc!.Value, records[1].CheckOutAtUtc!.Value));
        Assert.Contains(h.Db.AuditLogs.AsNoTracking(), a => a.Reason == "NightOpenedAfterMorningBlock");

        // Both stretches are worked time of the day they are filed under; last night stays absent —
        // the second half is never moved onto it, by the owner's rule.
        var options = new AppOptions { TimeZone = "Asia/Baku" };
        await new DailySummaryService(h.Db, options).GenerateForDateAsync(D0);
        await new DailySummaryService(h.Db, options).GenerateForDateAsync(D1);
        Assert.Equal(DailySummaryStatus.Absent,
            h.Db.DailySummaries.AsNoTracking().Single(s => s.SummaryDate == D0).Status);
        var d1 = h.Db.DailySummaries.AsNoTracking().Single(s => s.SummaryDate == D1);
        Assert.NotEqual(DailySummaryStatus.Absent, d1.Status);
        Assert.Equal(330 + 845, d1.WorkedMinutes);
    }

    [Fact]
    public async Task A_morning_tap_confirmed_as_an_exit_does_not_cost_tonights_arrival()
    {
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));
        await h.At(Utc(D1, 7, 46)).Scan(h.Poster());
        Assert.IsType<ConflictObjectResult>(await h.At(Utc(D1, 7, 55)).Scan(h.Poster()));   // «are you leaving?»
        Assert.Equal("CheckOut",
            Action(await h.At(Utc(D1, 7, 56)).Scan(h.Poster() with { ConfirmEarlyCheckOut = true })));

        Assert.Equal("CheckIn", Action(await h.At(Utc(D1, 17, 40)).Scan(h.Poster())));

        var records = await h.RecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.Null(records[1].CheckOutAtUtc);
    }

    [Fact]
    public async Task A_days_cover_closed_in_the_evening_still_refuses_a_further_scan()
    {
        // 07:46 → 17:51 is a day's work. A scan after it is the ordinary «artıq tamamlamısınız», unchanged.
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));
        await h.At(Utc(D1, 7, 46)).Scan(h.Poster());
        await h.At(Utc(D1, 17, 51)).Scan(h.Poster());

        Assert.IsType<ConflictObjectResult>(await h.At(Utc(D1, 18, 10)).Scan(h.Poster()));
        Assert.Single(await h.RecordsAsync());
    }

    [Fact]
    public async Task A_day_shift_with_a_finished_morning_still_refuses_an_afternoon_scan()
    {
        using var h = new Harness(new TimeOnly(8, 0), new TimeOnly(11, 0));
        await h.At(Utc(D1, 8, 0)).Scan(h.Poster());
        await h.At(Utc(D1, 11, 0)).Scan(h.Poster());

        Assert.IsType<ConflictObjectResult>(await h.At(Utc(D1, 17, 0)).Scan(h.Poster()));
        Assert.Single(await h.RecordsAsync());
    }

    [Theory]
    //          now      start   night  split  blocks open   in       out      expected
    [InlineData("17:55", "18:00", true, false, 1, false, "02:30", "08:00", true)]
    [InlineData("16:00", "18:00", true, false, 1, false, "02:30", "08:00", true)]   // two hours early: still the arrival
    [InlineData("15:59", "18:00", true, false, 1, false, "02:30", "08:00", false)]  // earlier: an afternoon mis-tap
    [InlineData("10:00", "18:00", true, false, 1, false, "02:30", "08:00", false)]  // a morning scan: never
    [InlineData("17:55", "18:00", true, false, 1, false, "07:46", "17:51", false)]  // a day's cover closed in the evening
    [InlineData("17:55", "18:00", true, false, 2, false, "02:30", "08:00", false)]  // the day already has two
    [InlineData("17:55", "18:00", true, false, 1, true, "02:30", "08:00", false)]   // open: that is a check-out
    [InlineData("17:55", "18:00", false, false, 1, false, "02:30", "08:00", false)] // not a night
    [InlineData("17:55", "18:00", true, true, 1, false, "02:30", "08:00", false)]   // a split day has its own rule
    [InlineData("10:30", "11:00", true, false, 1, false, "02:30", "08:00", false)]  // a «night» starting before noon
    public void The_evening_arrival_after_a_morning_stretch(string now, string start, bool overnight, bool split,
        int blocks, bool open, string blockIn, string blockOut, bool expected)
        => Assert.Equal(expected, NightScanRules.MayOpenNightAfterMorningBlock(
            TimeOnly.Parse(now), TimeOnly.Parse(start), overnight, split, blocks, open,
            TimeOnly.Parse(blockIn), TimeOnly.Parse(blockOut)));

    [Theory]
    [InlineData(0, 30, true)]   // taken in the first half
    [InlineData(2, 0, false)]   // taken in the second half
    public async Task An_offline_scan_is_placed_by_the_half_it_was_taken_in(int hour, int minute, bool lastNight)
    {
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));
        var taken = Utc(D1, hour, minute);

        var result = await h.At(Utc(D1, 9, 30)).Scan(h.Poster(offlineAtUtc: taken));

        Assert.Equal("CheckIn", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(lastNight ? D0 : D1, record.AttendanceDate);
        Assert.Equal(taken, record.CheckInAtUtc);
        Assert.True(record.WasOffline);
    }

    [Fact]
    public async Task An_offline_batch_from_the_second_half_syncs_as_one_day()
    {
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));

        Assert.Equal("CheckIn", Action(await h.At(Utc(D1, 9, 0)).Scan(h.Poster(offlineAtUtc: Utc(D1, 2, 30)))));
        Assert.Equal("CheckOut", Action(await h.At(Utc(D1, 9, 0)).Scan(h.Poster(offlineAtUtc: Utc(D1, 8, 0)))));

        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(D1, record.AttendanceDate);
        Assert.Equal((Utc(D1, 2, 30), Utc(D1, 8, 0)), (record.CheckInAtUtc!.Value, record.CheckOutAtUtc!.Value));
    }

    [Fact]
    public async Task Two_accounts_on_one_phone_are_each_placed_by_their_own_scan()
    {
        using var h = new Harness(new TimeOnly(18, 0), new TimeOnly(8, 0));
        var colleague = h.AddColleague(canShareDevice: true);

        Assert.Equal("CheckIn", Action(await h.As(h.EmployeeId, Utc(D1, 0, 30)).Scan(h.Poster())));  // first half
        Assert.Equal("CheckIn", Action(await h.As(colleague, Utc(D1, 2, 0)).Scan(h.Poster())));        // second half

        Assert.Equal(D0, Assert.Single(await h.RecordsAsync()).AttendanceDate);
        Assert.Equal(D1, Assert.Single(await h.RecordsOfAsync(colleague)).AttendanceDate);
    }

    [Fact]
    public async Task A_split_days_night_is_closed_in_the_morning_and_a_repeat_tap_opens_nothing()
    {
        // 07:00–11:00 and back 22:00–07:00. The morning exit closes the night stretch; a tap after it is a
        // repeat of that exit — decided by the LATEST of the day's exits, not whichever row comes first.
        using var h = new Harness(new TimeOnly(7, 0), new TimeOnly(11, 0),
            secondWindow: (new TimeOnly(22, 0), new TimeOnly(7, 0)));
        Assert.Equal("CheckIn", Action(await h.At(Utc(D0, 7, 0)).Scan(h.Poster())));
        Assert.Equal("CheckOut", Action(await h.At(Utc(D0, 11, 0)).Scan(h.Poster())));
        Assert.Equal("CheckIn", Action(await h.At(Utc(D0, 22, 0)).Scan(h.Poster())));
        Assert.Equal("CheckOut", Action(await h.At(Utc(D1, 7, 0)).Scan(h.Poster())));

        Assert.IsType<ConflictObjectResult>(await h.At(Utc(D1, 7, 30)).Scan(h.Poster()));

        var records = await h.RecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.All(records, r => Assert.Equal(D0, r.AttendanceDate));
    }

    [Fact]
    public async Task A_split_days_after_midnight_arrival_is_not_touched_by_this_rule()
    {
        // The rule reads the night's FIRST window; a split day's first window is its morning, so an
        // after-midnight arrival for the second window files under the new day exactly as before.
        using var h = new Harness(new TimeOnly(7, 0), new TimeOnly(11, 0),
            secondWindow: (new TimeOnly(22, 0), new TimeOnly(7, 0)));
        await h.At(Utc(D0, 7, 0)).Scan(h.Poster());
        await h.At(Utc(D0, 11, 0)).Scan(h.Poster());

        Assert.Equal("CheckIn", Action(await h.At(Utc(D1, 0, 30)).Scan(h.Poster())));
        Assert.Contains(await h.RecordsAsync(), r => r.AttendanceDate == D1);
    }

    // ─── stubs ───────────────────────────────────────────────────────────────────────────────────────

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("f");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private sealed class StubFace : IFaceMatchService
    {
        public bool Enabled => false;
        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));
        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default) => Task.FromResult(-1);
    }
}
