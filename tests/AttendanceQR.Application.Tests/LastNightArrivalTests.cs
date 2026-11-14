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
/// decision, 2026-11-14). 23:50–08:00 with fifteen minutes of grace:
///   23:55 → that day, on time · 00:03 → the day before, on time · 00:06 → the day before, late ·
///   08:00, the shift over → the ordinary rules.
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

        public Harness(TimeOnly start, TimeOnly end, int workDaysMask = 127, bool qrless = false)
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
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Gece Isci", Email = "gece@t.local",
                LocationId = LocationId, Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
                MonthlySalary = 3000m, ActivatedAtUtc = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            });
            Db.SaveChanges();
        }

        public AttendanceController At(DateTime serverUtc) =>
            new(Db, _qr, new AttendanceQueryService(Db), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(),
                new StubFace(), new DeviceBindingOptions { AutoBind = true }, new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()), NullLogger<AttendanceController>.Instance,
                summaryRebuild: Rebuild, clock: new FixedClock(serverUtc))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", EmployeeId.ToString())], "test")),
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

        public Task<List<AttendanceRecord>> RecordsAsync() =>
            Db.AttendanceRecords.AsNoTracking().OrderBy(r => r.CheckInAtUtc).ToListAsync();

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
    [InlineData(1, 7, 59, 0, AttendanceStatus.Late)]     // the shift is still running
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
    [InlineData("07:59", true, "23:50", "08:00", true, "23:50", true)]
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
