using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Elastik qrafik» (Schedule.IsFlexible), end to end: the session rule the scan follows, the day the nightly job
/// writes, the switch between modes, the «Qayıb yaz» refusal and the tabel — and, in every group, the control that a
/// fixed-hours colleague in the same company is judged exactly as before.
///
/// The week: Monday 5 – Friday 9 October 2026 (Baku time). Everybody has worked before (a scan on Thursday 1
/// October), so nobody is still «onboarding» and a day with no scan is judged, not skipped.
/// </summary>
public class FlexibleScheduleTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000f1e0");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private const double Lat = 40.4093;
    private const double Lng = 49.8671;

    private static readonly DateOnly First = new(2026, 10, 1);   // Thursday — the earlier scan
    private static readonly DateOnly Mon = new(2026, 10, 5);
    private static readonly DateOnly Tue = new(2026, 10, 6);
    private static readonly DateOnly Wed = new(2026, 10, 7);
    private static readonly DateOnly Thu = new(2026, 10, 8);

    private static DateTime Utc(DateOnly date, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Baku);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();     // on the flexible shift
        public Guid FixedId { get; } = Guid.NewGuid();        // a colleague on the branch's fixed 09:00–18:00
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Guid ScheduleId { get; } = Guid.NewGuid();
        private readonly IQrTokenService _qr = new QrTokenService(Options.Create(new QrTokenOptions
        {
            Secret = "test-secret-key-for-flexible-schedule-tests", TtlSeconds = 300,
        }));

        public Harness(int? normMinutes = 480, FlexibleNormPeriod period = FlexibleNormPeriod.Day, int normMask = 62,
            string dbName = "")
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(dbName == "" ? $"flex-{Guid.NewGuid()}" : dbName)
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Ofis", Latitude = Lat, Longitude = Lng, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15,
                WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            Db.Schedules.Add(new Schedule
            {
                Id = ScheduleId, TenantId = TenantId, Name = "Elastik",
                ShiftStart = new TimeOnly(0, 0), ShiftEnd = new TimeOnly(23, 59), LateThresholdMinutes = 15,
                WorkDaysMask = normMask, IsFlexible = true, FlexibleNormMinutes = normMinutes, FlexibleNormPeriod = period,
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            var activated = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Elastik İşçi", Email = "flex@t.local", LocationId = LocationId,
                ScheduleId = ScheduleId, Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
                MonthlySalary = 3000m, ActivatedAtUtc = activated,
            });
            Db.Employees.Add(new Employee
            {
                Id = FixedId, TenantId = TenantId, FullName = "Sabit İşçi", Email = "fixed@t.local", LocationId = LocationId,
                Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x", MonthlySalary = 3000m, ActivatedAtUtc = activated,
            });
            Db.Employees.Add(new Employee
            {
                Id = AdminId, TenantId = TenantId, FullName = "Admin", Email = "admin@t.local", LocationId = LocationId,
                Role = EmployeeRole.Admin, IsActive = true, PasswordHash = "x", ActivatedAtUtc = activated,
            });
            // Both have worked before, so a missing day is judged rather than read as onboarding.
            foreach (var id in new[] { EmployeeId, FixedId })
                Db.AttendanceRecords.Add(new AttendanceRecord
                {
                    TenantId = TenantId, EmployeeId = id, LocationId = LocationId, AttendanceDate = First,
                    CheckInAtUtc = Utc(First, 9), CheckOutAtUtc = Utc(First, 17), Status = AttendanceStatus.OnTime,
                });
            Db.SaveChanges();
        }

        public AttendanceController At(DateTime serverUtc, Guid? who = null) =>
            new(Db, _qr, new AttendanceQueryService(Db), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(),
                new StubFace(), new DeviceBindingOptions { AutoBind = true }, new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()), NullLogger<AttendanceController>.Instance,
                clock: new FixedClock(serverUtc))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", (who ?? EmployeeId).ToString())], "test")),
                    },
                },
            };

        public ScanRequest Poster(DateTime? offlineAtUtc = null, Guid? clientScanId = null, bool confirm = false,
            string? longSession = null) =>
            new(_qr.Generate(LocationId, 1), "device-fp-1", Lat, Lng,
                ClientScanId: clientScanId, ClientTimestampUtc: offlineAtUtc, Offline: offlineAtUtc is not null,
                ConfirmEarlyCheckOut: confirm, LongSession: longSession);

        public Task<IActionResult> ScanAt(DateTime utc, Guid? who = null, string? longSession = null, Guid? clientScanId = null) =>
            At(utc, who).Scan(Poster(clientScanId: clientScanId, longSession: longSession));

        public List<AttendanceRecord> Sessions(Guid? who = null) =>
            Db.AttendanceRecords.AsNoTracking()
                .Where(r => r.EmployeeId == (who ?? EmployeeId) && r.AttendanceDate > First)
                .OrderBy(r => r.CheckInAtUtc).ToList();

        public async Task<DailySummary?> SummaryAsync(DateOnly date, Guid? who = null)
        {
            await new DailySummaryService(Db, new AppOptions { TimeZone = "Asia/Baku" }).GenerateForDateAsync(date);
            return Db.DailySummaries.AsNoTracking().SingleOrDefault(s => s.SummaryDate == date && s.EmployeeId == (who ?? EmployeeId));
        }

        public T As<T>(T controller) where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("sub", AdminId.ToString()), new Claim("role", EmployeeRole.Admin.ToString()),
                    }, "test")),
                },
            };
            return controller;
        }

        public void Dispose() => Db.Dispose();
    }

    private static string? Error(IActionResult r) =>
        r is ObjectResult { Value: { } v } ? v.GetType().GetProperty("error")?.GetValue(v) as string : null;

    private static object? Field(IActionResult r, string name) =>
        r is ObjectResult { Value: { } v } ? v.GetType().GetProperty(name)?.GetValue(v) : null;

    // ─── the rule on its own ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_session_rule_closes_a_night_asks_about_a_long_one_and_leaves_yesterday_morning_open()
    {
        const FlexibleScanRules.Outcome In = FlexibleScanRules.Outcome.CheckIn, Out = FlexibleScanRules.Outcome.CheckOut,
            Ask = FlexibleScanRules.Outcome.AskLongSession;
        // Nothing open → a session starts.
        Assert.Equal(In, FlexibleScanRules.Decide(null, null, Utc(Mon, 9), Mon));
        // Up to twelve hours closes, whatever the calendar: a day's work, a night shift, a 20:00–08:00 guard night.
        Assert.Equal(Out, FlexibleScanRules.Decide(Utc(Mon, 9), Mon, Utc(Mon, 21), Mon));
        Assert.Equal(Out, FlexibleScanRules.Decide(Utc(Mon, 22), Mon, Utc(Tue, 6), Tue));
        Assert.Equal(Out, FlexibleScanRules.Decide(Utc(Mon, 20), Mon, Utc(Tue, 8), Tue));
        // Twelve to sixteen hours: the end of a long night, or an arrival after a forgotten check-out — asked.
        Assert.Equal(Ask, FlexibleScanRules.Decide(Utc(Mon, 18), Mon, Utc(Tue, 8), Tue));       // 14 h guard night
        Assert.Equal(Ask, FlexibleScanRules.Decide(Utc(Mon, 14), Mon, Utc(Tue, 5), Tue));       // forgot at the afternoon's end
        Assert.Equal(Ask, FlexibleScanRules.Decide(Utc(Mon, 18), Mon, Utc(Tue, 10), Tue));      // sixteen exactly
        // Past sixteen hours since an earlier day: a forgotten check-out, left open; a new session starts.
        Assert.Equal(In, FlexibleScanRules.Decide(Utc(Mon, 18), Mon, Utc(Tue, 10, 1), Tue));
        Assert.Equal(In, FlexibleScanRules.Decide(Utc(Mon, 9), Mon, Utc(Tue, 9), Tue));
        // The same day is never «forgotten» by the rule — one day holds one open session — so a long one is asked.
        Assert.Equal(Ask, FlexibleScanRules.Decide(Utc(Mon, 7), Mon, Utc(Mon, 21, 1), Mon));
        Assert.Equal(Ask, FlexibleScanRules.Decide(Utc(Mon, 0, 30), Mon, Utc(Mon, 23), Mon));
        // An offline scan from before the open session began cannot close it.
        Assert.Equal(FlexibleScanRules.Outcome.OutOfOrder, FlexibleScanRules.Decide(Utc(Mon, 12), Mon, Utc(Mon, 9), Mon));
        // The screens: a night is running until sixteen hours, never «forgotten» before.
        Assert.False(FlexibleScanRules.IsForgotten(Utc(Mon, 18), Mon, Utc(Tue, 9), Tue));
        Assert.True(FlexibleScanRules.IsForgotten(Utc(Mon, 9), Mon, Utc(Tue, 9), Tue));
    }

    [Fact]
    public void A_flexible_shift_has_no_working_day_no_hours_and_only_a_norm()
    {
        var schedule = new Schedule
        {
            Name = "Elastik", IsFlexible = true, FlexibleNormMinutes = 480, WorkDaysMask = 62,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15,
            SecondShiftStart = new TimeOnly(22, 0), SecondShiftEnd = new TimeOnly(6, 0), DayHours = "6=10:00-14:00",
        };
        var shift = EffectiveShift.Resolve(new Employee(), schedule, new Location { WorkDaysMask = 127 });

        Assert.True(shift.IsFlexible);
        // Whatever the row holds, the window is the whole day and nobody is ever due.
        Assert.Equal((new TimeOnly(0, 0), new TimeOnly(23, 59)), shift.HoursOn(Mon));
        Assert.False(shift.HasSecondWindow);
        for (var d = Mon; d < Mon.AddDays(7); d = d.AddDays(1))
            Assert.False(shift.IsWorkingDay(d));
        // A daily norm applies on the mask's days (Mon–Fri here), never at the weekend.
        Assert.True(shift.IsNormDay(Mon));
        Assert.False(shift.IsNormDay(new DateOnly(2026, 10, 10)));   // Saturday
        // A weekly norm has no per-day days.
        Assert.False(EffectiveShift.Resolve(new Employee(), new Schedule
        {
            Name = "E", IsFlexible = true, FlexibleNormMinutes = 2400, FlexibleNormPeriod = FlexibleNormPeriod.Week, WorkDaysMask = 62,
        }, new Location()).IsNormDay(Mon));
    }

    [Fact]
    public void A_late_night_session_is_neither_late_nor_overtime_nor_early()
    {
        var shift = EffectiveShift.Resolve(new Employee(), new Schedule { Name = "E", IsFlexible = true }, new Location());
        var record = new AttendanceRecord { AttendanceDate = Mon, CheckInAtUtc = Utc(Mon, 23, 50), CheckOutAtUtc = Utc(Tue, 7, 50) };

        var day = AttendanceCalculator.Compute(record, shift, Baku, isWorkingDay: true, DailySummaryStatus.Absent);

        Assert.Equal(DailySummaryStatus.OnTime, day.Status);
        Assert.Equal(480, day.WorkedMinutes);
        Assert.Equal((0, 0, 0, 0), (day.LateMinutes, day.OvertimeMinutes, day.EarlyLeaveMinutes, day.EarlyArriveMinutes));
    }

    [Fact]
    public void Switching_mode_or_norm_is_kept_as_a_revision()
    {
        var fixedShift = new Schedule { Name = "Ofis", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0) };
        var flexible = ShiftResolver.Snapshot(fixedShift);
        flexible.IsFlexible = true;
        Assert.True(ShiftResolver.JudgesDaysDifferently(fixedShift, flexible));

        var normed = ShiftResolver.Snapshot(flexible);
        normed.FlexibleNormMinutes = 480;
        Assert.True(ShiftResolver.JudgesDaysDifferently(flexible, normed));
        Assert.True(ShiftResolver.Snapshot(normed).IsFlexible);   // the snapshot carries the mode
    }

    [Fact]
    public void The_plan_history_names_a_flexible_shift_without_inventing_hours()
    {
        var flexible = new Schedule { Id = Guid.NewGuid(), Name = "Barmenlər", IsFlexible = true, ShiftStart = new TimeOnly(0, 0), ShiftEnd = new TimeOnly(23, 59) };
        var office = new Schedule { Id = Guid.NewGuid(), Name = "Ofis", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0) };
        var catalogue = new Dictionary<Guid, Schedule> { [flexible.Id] = flexible, [office.Id] = office };

        Assert.Equal("Barmenlər (sərbəst qrafik)", StandingShifts.DayLabel(flexible.Id.ToString(), catalogue));
        Assert.Equal("Ofis 09:00–18:00", StandingShifts.DayLabel(office.Id.ToString(), catalogue));
        Assert.Equal("Sərbəst iş qrafiki", StandingShifts.FlexibleName("Sərbəst iş qrafiki"));   // not «… (sərbəst qrafik)»
        Assert.Equal("Elastik", StandingShifts.FlexibleName("Elastik"));   // a shift still named the old way
        Assert.Equal("Sərbəst iş qrafiki", StandingShifts.FlexibleName(null));
    }

    // ─── the scan ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_arrival_at_any_hour_is_never_late()
    {
        using var h = new Harness();

        var result = await h.ScanAt(Utc(Mon, 11, 40));

        Assert.Null(Error(result));
        Assert.Equal(false, Field(result, "late"));
        Assert.Equal(AttendanceStatus.OnTime, Assert.Single(h.Sessions()).Status);
    }

    [Fact]
    public async Task A_day_is_the_sum_of_its_sessions_and_the_break_between_them_is_not_paid()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 9));
        var lunch = await h.ScanAt(Utc(Mon, 12));
        await h.ScanAt(Utc(Mon, 13));
        var home = await h.ScanAt(Utc(Mon, 18));

        Assert.Equal(false, Field(lunch, "earlyDeparture"));
        Assert.Equal(false, Field(home, "earlyDeparture"));
        var sessions = h.Sessions();
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(Mon, s.AttendanceDate));
        Assert.All(sessions, s => Assert.NotNull(s.CheckOutAtUtc));

        var day = await h.SummaryAsync(Mon);
        Assert.Equal(DailySummaryStatus.OnTime, day!.Status);
        Assert.Equal(8 * 60, day.WorkedMinutes);                     // 3 h + 5 h — the hour at lunch is not paid
        Assert.Equal((0, 0, 0), (day.LateMinutes, day.OvertimeMinutes, day.EarlyLeaveMinutes));
    }

    [Fact]
    public async Task A_session_across_midnight_is_one_session_on_the_day_it_started_and_counted_once()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 22));
        var morning = await h.ScanAt(Utc(Tue, 6));

        Assert.Null(Error(morning));
        var only = Assert.Single(h.Sessions());
        Assert.Equal(Mon, only.AttendanceDate);
        Assert.Equal(Utc(Tue, 6), only.CheckOutAtUtc);

        Assert.Equal(8 * 60, (await h.SummaryAsync(Mon))!.WorkedMinutes);
        var tuesday = await h.SummaryAsync(Tue);
        Assert.Equal(0, tuesday!.WorkedMinutes);                     // not written twice
        Assert.Equal(DailySummaryStatus.DayOff, tuesday.Status);
    }

    [Fact]
    public async Task A_long_night_is_asked_about_and_closed_once_the_person_says_they_are_leaving()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 18));
        var scanId = Guid.NewGuid();

        var asked = await h.ScanAt(Utc(Tue, 8), clientScanId: scanId);
        Assert.Equal("ConfirmLongSession", Error(asked));
        Assert.Equal(14.0, Field(asked, "hours"));
        Assert.Equal(false, Field(asked, "sameDay"));
        Assert.Null(Assert.Single(h.Sessions()).CheckOutAtUtc);         // nothing written while asking

        var answered = await h.ScanAt(Utc(Tue, 8), longSession: "close", clientScanId: scanId);
        Assert.Null(Error(answered));
        Assert.Equal("CheckOut", Field(answered, "action"));
        var night = Assert.Single(h.Sessions());
        Assert.Equal(Mon, night.AttendanceDate);                          // filed under the day it began
        Assert.Equal(Utc(Tue, 8), night.CheckOutAtUtc);
        Assert.Equal(14 * 60, (await h.SummaryAsync(Mon))!.WorkedMinutes);
    }

    [Fact]
    public async Task A_forgotten_afternoon_stays_open_when_the_person_says_they_are_arriving_and_two_open_sessions_do_not_mix()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 14));                                     // never signed out

        Assert.Equal("ConfirmLongSession", Error(await h.ScanAt(Utc(Tue, 5))));
        var arrival = await h.ScanAt(Utc(Tue, 5), longSession: "new");
        Assert.Equal("CheckIn", Field(arrival, "action"));

        var sessions = h.Sessions();
        Assert.Equal(2, sessions.Count);
        Assert.Null(sessions[0].CheckOutAtUtc);                           // Monday: open, for the admin — no exit invented
        Assert.Equal((Tue, (DateTime?)null), (sessions[1].AttendanceDate, sessions[1].CheckOutAtUtc));
        Assert.Contains(h.Db.AuditLogs.AsNoTracking(), a => a.Reason == "FlexibleForgottenReported|2026-10-05");

        // Two sessions are open now. The next scan closes TODAY's (the newest), never Monday's.
        var leaving = await h.ScanAt(Utc(Tue, 13));
        Assert.Equal("CheckOut", Field(leaving, "action"));
        sessions = h.Sessions();
        Assert.Null(sessions[0].CheckOutAtUtc);
        Assert.Equal(Utc(Tue, 13), sessions[1].CheckOutAtUtc);
        Assert.Equal(8 * 60, (await h.SummaryAsync(Tue))!.WorkedMinutes);
        Assert.Equal(DailySummaryStatus.Incomplete, (await h.SummaryAsync(Mon))!.Status);   // open ≠ 0 saat
    }

    [Fact]
    public async Task A_long_session_from_this_morning_is_asked_about_and_never_doubled_on_the_same_day()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 7));

        var asked = await h.ScanAt(Utc(Mon, 20));
        Assert.Equal("ConfirmLongSession", Error(asked));
        Assert.Equal(true, Field(asked, "sameDay"));

        // «I forgot to sign out»: one day holds one open session, so nothing is opened beside it and nothing closed —
        // the arrival is on the Problems screen with this morning's time, for the admin.
        var forgot = await h.ScanAt(Utc(Mon, 20), longSession: "new");
        Assert.Equal("ForgottenCheckOutToday", Error(forgot));
        var open = Assert.Single(h.Sessions());
        Assert.Null(open.CheckOutAtUtc);
        Assert.Contains(h.Db.AuditLogs.AsNoTracking(), a => a.EventType == AuditEventType.CheckInRejected
                                                            && a.Reason == "FlexibleForgottenToday|07:00");

        // «Yes, a long day»: closed at this scan.
        var closed = await h.ScanAt(Utc(Mon, 20, 5), longSession: "close");
        Assert.Equal("CheckOut", Field(closed, "action"));
        Assert.Equal(13 * 60 + 5, (await h.SummaryAsync(Mon))!.WorkedMinutes);
    }

    [Fact]
    public async Task An_offline_scan_on_a_long_session_is_refused_and_reported_never_guessed()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 18));

        var replay = await h.At(Utc(Tue, 9)).Scan(h.Poster(offlineAtUtc: Utc(Tue, 8)));
        Assert.Equal("ConfirmLongSession", Error(replay));
        Assert.Null(Assert.Single(h.Sessions()).CheckOutAtUtc);           // still open: neither closed nor doubled
        Assert.Contains(h.Db.AuditLogs.AsNoTracking(), a => a.Reason == "FlexibleLongSessionOffline");
    }

    [Fact]
    public async Task A_forgotten_check_out_stays_open_no_exit_is_invented_and_the_next_day_starts_fresh()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 9));
        var tuesday = await h.ScanAt(Utc(Tue, 9, 30));

        Assert.Null(Error(tuesday));
        var sessions = h.Sessions();
        Assert.Equal(2, sessions.Count);
        Assert.Null(sessions[0].CheckOutAtUtc);                      // Monday: still open, for the admin
        Assert.Equal(Tue, sessions[1].AttendanceDate);
        Assert.Contains(h.Db.AuditLogs.AsNoTracking(), a => a.Reason == "FlexibleStaleSessionLeftOpen|2026-10-05");

        // Open is not «0 saat»: the day reads Incomplete, distinct from a day with no scan (DayOff).
        var monday = await h.SummaryAsync(Mon);
        Assert.Equal(DailySummaryStatus.Incomplete, monday!.Status);
        Assert.Equal(0, monday.WorkedMinutes);
    }

    [Fact]
    public async Task The_double_tap_and_the_early_exit_question_still_guard_a_flexible_check_out()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 9));

        Assert.Equal("TooSoonToCheckOut", Error(await h.ScanAt(Utc(Mon, 9, 2))));
        var early = await h.ScanAt(Utc(Mon, 10));
        Assert.Equal("ConfirmEarlyCheckOut", Error(early));
        // The phone then says «iş sessiyanız bağlanacaq», not «iş gününüz»: only this session ends.
        Assert.Equal(true, Field(early, "flexible"));
        Assert.Null(Single().CheckOutAtUtc);

        var confirmed = await h.At(Utc(Mon, 10)).Scan(h.Poster(confirm: true));
        Assert.Null(Error(confirmed));
        Assert.Equal(Utc(Mon, 10), Single().CheckOutAtUtc);

        // A tap a minute after leaving is the «did it work?» retry, not a new session — and not «the day is over»
        // either: a flexible person may start another session a few minutes later.
        Assert.Equal("TooSoonAfterCheckOut", Error(await h.ScanAt(Utc(Mon, 10, 1))));
        Assert.Single(h.Sessions());
        Assert.Equal("CheckIn", Field(await h.ScanAt(Utc(Mon, 10, 6)), "action"));   // five minutes on: a new session
        Assert.Equal(2, h.Sessions().Count);

        AttendanceRecord Single() => h.Sessions()[0];
    }

    [Fact]
    public async Task Offline_scans_replayed_later_count_once_and_an_out_of_order_one_is_refused()
    {
        using var h = new Harness();
        var arrive = Guid.NewGuid();
        var leave = Guid.NewGuid();
        // The phone was offline all day; the queue drains at 19:00.
        await h.At(Utc(Mon, 19)).Scan(h.Poster(Utc(Mon, 9), arrive));
        await h.At(Utc(Mon, 19)).Scan(h.Poster(Utc(Mon, 17), leave));
        // …and drains again (the reply was lost): nothing changes.
        await h.At(Utc(Mon, 19, 5)).Scan(h.Poster(Utc(Mon, 17), leave));

        var only = Assert.Single(h.Sessions());
        Assert.Equal((Utc(Mon, 9), Utc(Mon, 17)), (only.CheckInAtUtc!.Value, only.CheckOutAtUtc!.Value));

        // Now in at 12:00 on Tuesday, and a scan from 09:00 that morning arrives late: it cannot close a session
        // that had not begun yet, and it is not guessed at.
        await h.ScanAt(Utc(Tue, 12));
        var stale = await h.At(Utc(Tue, 12, 30)).Scan(h.Poster(Utc(Tue, 9), Guid.NewGuid()));
        Assert.Equal("OutOfOrder", Error(stale));
        Assert.Null(h.Sessions().Last().CheckOutAtUtc);
    }

    [Fact]
    public async Task A_fixed_colleague_on_the_same_poster_is_judged_exactly_as_before()
    {
        using var h = new Harness();
        // Late on fixed hours: 09:00 + 15 min grace, in at 09:40.
        var arrival = await h.ScanAt(Utc(Mon, 9, 40), h.FixedId);
        Assert.Equal(true, Field(arrival, "late"));
        // The check-out says it is a fixed day, so the phone's card keeps «Sabaha qədər!».
        Assert.Equal(false, Field(await h.ScanAt(Utc(Mon, 18), h.FixedId), "flexible"));
        // A fixed shift still refuses a third scan of a finished day.
        Assert.Equal("AlreadyCompleted", Error(await h.ScanAt(Utc(Mon, 19), h.FixedId)));

        var day = await h.SummaryAsync(Mon, h.FixedId);
        Assert.Equal(DailySummaryStatus.Late, day!.Status);
        Assert.Equal(40, day.LateMinutes);
    }

    // ─── a day nobody came ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_day_with_no_scan_is_zero_hours_never_an_absence_but_the_fixed_colleague_is_absent()
    {
        using var h = new Harness();

        var flexible = await h.SummaryAsync(Wed);
        var fixedDay = h.Db.DailySummaries.AsNoTracking().Single(s => s.SummaryDate == Wed && s.EmployeeId == h.FixedId);

        Assert.Equal(DailySummaryStatus.DayOff, flexible!.Status);
        Assert.Equal(0, flexible.WorkedMinutes);
        Assert.Equal(DailySummaryStatus.Absent, fixedDay.Status);
    }

    [Fact]
    public async Task A_manager_cannot_write_Qayib_on_a_flexible_day_but_still_can_on_a_fixed_one()
    {
        using var h = new Harness();
        var marks = h.As(new AbsenceMarksController(h.Db, new StubSummary()));

        var flexible = await marks.Mark(new AbsenceMarkRequest(h.EmployeeId, Wed, "Gəlmədi, xəbər vermədi"));
        Assert.Equal("FlexibleSchedule", Error(flexible));

        var fixedDay = await marks.Mark(new AbsenceMarkRequest(h.FixedId, Wed, "Gəlmədi, xəbər vermədi"));
        Assert.IsType<OkObjectResult>(fixedDay);
    }

    // ─── switching mode ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Switching_a_shift_to_flexible_keeps_past_days_on_fixed_hours_and_is_audited()
    {
        using var h = new Harness();
        var office = new Schedule
        {
            TenantId = TenantId, Name = "Ofis", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 62, CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        h.Db.Schedules.Add(office);
        h.Db.SaveChanges();
        var schedules = h.As(new SchedulesController(h.Db, new AppOptions { TimeZone = "Asia/Baku" }));

        var switched = await schedules.Update(office.Id, new ScheduleRequest(
            "Ofis", "", "", IsFlexible: true, FlexibleNormMinutes: 480, FlexibleNormPeriod: "day"));
        Assert.Null(Error(switched));

        var all = await h.Db.Schedules.AsNoTracking().ToDictionaryAsync(s => s.Id);
        var today = ScheduleRevisions.Today(Baku);
        // The day before the switch is still judged on 09:00–18:00; from today, flexible.
        Assert.False(ShiftResolver.ScheduleOn(office.Id, office.Id, today.AddDays(-1), all)!.IsFlexible);
        Assert.Equal(new TimeOnly(9, 0), ShiftResolver.ScheduleOn(office.Id, office.Id, today.AddDays(-1), all)!.ShiftStart);
        Assert.True(ShiftResolver.ScheduleOn(office.Id, office.Id, today, all)!.IsFlexible);

        var audit = Assert.Single(h.Db.AuditLogs.AsNoTracking().Where(a => a.EventType == AuditEventType.ScheduleModeChanged));
        Assert.Contains("Sabit saatlar → Sərbəst iş qrafiki (norma 8 saat/gün)", audit.Reason);
        Assert.Contains(h.AdminId.ToString(), audit.Reason);

        // A client that knows nothing of the mode (the manager's form, an old app) saves the shift: it stays flexible.
        await schedules.Update(office.Id, new ScheduleRequest("Ofis yeni ad", "09:00", "18:00", WorkDaysMask: 62));
        var after = await h.Db.Schedules.AsNoTracking().SingleAsync(s => s.Id == office.Id);
        Assert.True(after.IsFlexible);
        Assert.Equal(480, after.FlexibleNormMinutes);
        Assert.Single(h.Db.AuditLogs.AsNoTracking().Where(a => a.EventType == AuditEventType.ScheduleModeChanged));

        // And back: fixed hours again, audited the other way.
        await schedules.Update(office.Id, new ScheduleRequest("Ofis", "09:00", "18:00", WorkDaysMask: 62, IsFlexible: false));
        var back = await h.Db.Schedules.AsNoTracking().SingleAsync(s => s.Id == office.Id);
        Assert.False(back.IsFlexible);
        Assert.Null(back.FlexibleNormMinutes);
        Assert.Contains(h.Db.AuditLogs.AsNoTracking(), a => a.EventType == AuditEventType.ScheduleModeChanged
                                                           && a.Reason!.Contains("Sərbəst iş qrafiki (norma 8 saat/gün) → Sabit saatlar"));
    }

    [Fact]
    public async Task A_norm_out_of_range_is_refused()
    {
        using var h = new Harness();
        var schedules = h.As(new SchedulesController(h.Db, new AppOptions { TimeZone = "Asia/Baku" }));

        Assert.Equal("FlexibleNormTooLarge", Error(await schedules.Create(new ScheduleRequest(
            "Elastik 2", "", "", IsFlexible: true, FlexibleNormMinutes: 25 * 60))));
        Assert.Equal("FlexibleNormPeriodInvalid", Error(await schedules.Create(new ScheduleRequest(
            "Elastik 3", "", "", IsFlexible: true, FlexibleNormMinutes: 480, FlexibleNormPeriod: "month"))));
        var weekly = await schedules.Create(new ScheduleRequest(
            "Elastik 40", "", "", IsFlexible: true, FlexibleNormMinutes: 40 * 60, FlexibleNormPeriod: "week"));
        Assert.Null(Error(weekly));
        Assert.Equal("week", Field(weekly, "flexibleNormPeriod"));
    }

    // ─── the tabel ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_tabel_prints_hours_zero_and_open_on_flexible_days_and_codes_on_fixed_ones()
    {
        using var h = new Harness(normMinutes: 480);
        // Monday 09:00–17:00; Tuesday nothing; Wednesday in at 10:00 and never out.
        await h.ScanAt(Utc(Mon, 9));
        await h.ScanAt(Utc(Mon, 17));
        await h.ScanAt(Utc(Wed, 10));
        for (var d = First; d <= new DateOnly(2026, 10, 9); d = d.AddDays(1))
            await new DailySummaryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" }).GenerateForDateAsync(d);

        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" },
            new FixedClock(Utc(new DateOnly(2026, 10, 10), 12)));
        var (_, tabel) = await reports.GetTabelAsync(2026, 10, null, h.AdminId, EmployeeRole.Admin);

        var flex = tabel!.Rows.Single(r => r.EmployeeId == h.EmployeeId);
        Assert.NotNull(flex.FlexibleCells);
        Assert.Equal("8", flex.FlexibleCells![Mon.Day - 1]);
        Assert.Equal("0", flex.FlexibleCells[Tue.Day - 1]);
        Assert.Equal("Açıq", flex.FlexibleCells[Wed.Day - 1]);
        Assert.Equal(0, flex.AbsentDays);
        // Norm: 8 h on each Monday–Friday from the 1st to today (the 10th, a Saturday) — 1, 2, 5, 6, 7, 8, 9.
        Assert.Equal(56, flex.NormHours);

        var fixedRow = tabel.Rows.Single(r => r.EmployeeId == h.FixedId);
        Assert.Null(fixedRow.FlexibleCells);
        Assert.Null(fixedRow.NormHours);
        Assert.Equal("Q", fixedRow.Days[Tue.Day - 1]);
    }

    [Fact]
    public async Task A_weekly_norm_is_compared_week_by_week_and_never_divided_into_days()
    {
        using var h = new Harness(normMinutes: 40 * 60, period: FlexibleNormPeriod.Week);
        // Week of 5 October: Monday 8 h, Tuesday 10 h, Wednesday nothing, Thursday 10 h — however it is spread.
        await h.ScanAt(Utc(Mon, 9)); await h.ScanAt(Utc(Mon, 17));
        await h.ScanAt(Utc(Tue, 9)); await h.ScanAt(Utc(Tue, 19));
        await h.ScanAt(Utc(Thu, 10)); await h.ScanAt(Utc(Thu, 20));
        for (var d = First; d <= new DateOnly(2026, 10, 9); d = d.AddDays(1))
            await new DailySummaryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" }).GenerateForDateAsync(d);

        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" },
            new FixedClock(Utc(new DateOnly(2026, 10, 10), 12)));
        var (_, tabel) = await reports.GetTabelAsync(2026, 10, null, h.AdminId, EmployeeRole.Admin);
        var flex = tabel!.Rows.Single(r => r.EmployeeId == h.EmployeeId);

        Assert.Null(flex.NormHours);                      // no month figure made of sevenths
        Assert.Equal(40, flex.WeeklyNormHours);
        var weeks = flex.NormWeeks!;
        Assert.Equal(2, weeks.Count);
        // 28 Sep – 4 Oct reaches back into September: the whole week, the 1st's eight hours in it.
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4), 40.0, 8.0, true),
            (weeks[0].From, weeks[0].To, weeks[0].NormHours, weeks[0].WorkedHours, weeks[0].Finished));
        // This week, still running: 8 + 10 + 10 against the full 40, not against 6 days × 40/7.
        Assert.Equal((Mon, 40.0, 28.0, false), (weeks[1].From, weeks[1].NormHours, weeks[1].WorkedHours, weeks[1].Finished));

        var fixedRow = tabel.Rows.Single(r => r.EmployeeId == h.FixedId);
        Assert.Null(fixedRow.NormWeeks);
        Assert.Null(fixedRow.WeeklyNormHours);
    }

    // ─── «İşçi Statistikaları» ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hr_stats_take_flexible_days_out_of_the_attendance_rate_instead_of_reading_100_percent()
    {
        using var h = new Harness();
        await h.ScanAt(Utc(Mon, 9)); await h.ScanAt(Utc(Mon, 17));
        for (var d = First; d <= new DateOnly(2026, 10, 9); d = d.AddDays(1))
            await new DailySummaryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" }).GenerateForDateAsync(d);

        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" },
            new FixedClock(Utc(new DateOnly(2026, 10, 10), 12)));
        var (_, stats) = await reports.GetHrStatsAsync(First, new DateOnly(2026, 10, 9), null, h.AdminId, EmployeeRole.Admin);

        var flex = stats!.Employees.Single(e => e.EmployeeId == h.EmployeeId);
        Assert.Null(flex.AttendancePercent);              // nobody was due — there is no rate, not 100 %
        Assert.Null(flex.OnTimePercent);                  // nor a punctuality one
        Assert.Equal(0, flex.WorkedDays);
        Assert.Equal(2, flex.FlexibleWorkedDays);         // the 1st and Monday, counted on their own

        var fixedOne = stats.Employees.Single(e => e.EmployeeId == h.FixedId);
        Assert.Equal(1, fixedOne.WorkedDays);             // the 1st; every other day of the week is a Qayıb
        Assert.True(fixedOne.AbsentDays > 0);
        Assert.True(fixedOne.AttendancePercent < 100);
        // The company's rate is the fixed staff's alone: the flexible person adds nothing to either side.
        Assert.Equal(fixedOne.AttendancePercent, stats.AttendancePercent);
    }

    // ─── another company ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Another_company_cannot_see_or_use_this_companys_flexible_shift()
    {
        var name = $"flex-two-{Guid.NewGuid()}";
        using var a = new Harness(dbName: name);

        // Company B, same database, a person whose ScheduleId happens to point at A's flexible shift.
        var otherTenant = Guid.NewGuid();
        var ctxB = new TenantContext();
        ctxB.Resolve(otherTenant);
        using var dbB = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options, ctxB);
        var locB = new Location
        {
            TenantId = otherTenant, Name = "B", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
        };
        dbB.Locations.Add(locB);
        var personB = new Employee
        {
            TenantId = otherTenant, FullName = "B İşçi", Email = "b@b.local", LocationId = locB.Id, ScheduleId = a.ScheduleId,
            Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
        };
        dbB.Employees.Add(personB);
        await dbB.SaveChangesAsync();

        // B's resolver never sees A's shift: B's person is on B's branch hours, fixed.
        var scheduleSeenByB = await ShiftResolver.ScheduleOnAsync(dbB, personB.Id, personB.ScheduleId, Mon, default);
        Assert.Null(scheduleSeenByB);
        Assert.False(EffectiveShift.Resolve(personB, scheduleSeenByB, locB).IsFlexible);
    }

    // ─── the day's board and its Excel ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_board_sends_the_last_departure_every_session_and_the_servers_own_hours_which_the_summary_agrees_with()
    {
        using var h = new Harness();
        // Monday: three sessions, 09:00–12:00, 13:00–18:00, 19:00–21:30 — 3 h + 5 h + 2½ h, the breaks unpaid.
        await h.ScanAt(Utc(Mon, 9));
        var firstOut = await h.ScanAt(Utc(Mon, 12));
        Assert.Equal(true, Field(firstOut, "flexible"));   // the phone's card: no «Sabaha qədər!»
        await h.ScanAt(Utc(Mon, 13)); await h.ScanAt(Utc(Mon, 18));
        await h.ScanAt(Utc(Mon, 19)); await h.ScanAt(Utc(Mon, 21, 30));
        // Tuesday: a morning, then a night across midnight — 09:00–12:00 and 22:00–06:00, both Tuesday's.
        await h.ScanAt(Utc(Tue, 9)); await h.ScanAt(Utc(Tue, 12));
        await h.ScanAt(Utc(Tue, 22)); await h.ScanAt(Utc(Wed, 6));
        // Thursday: 09:00–12:00, then 13:00 and never closed — a forgotten check-out on a past day.
        await h.ScanAt(Utc(Thu, 9)); await h.ScanAt(Utc(Thu, 12));
        await h.ScanAt(Utc(Thu, 13));

        var reports = new ReportQueryService(h.Db, new AppOptions { TimeZone = "Asia/Baku" },
            new FixedClock(Utc(new DateOnly(2026, 10, 10), 12)));
        async Task<DayAttendanceRow> Board(DateOnly d) =>
            (await reports.GetTodayAttendanceAsync(h.AdminId, EmployeeRole.Admin, d)).Single(r => r.EmployeeId == h.EmployeeId);

        var mon = await Board(Mon);
        Assert.Equal(Utc(Mon, 9), mon.CheckInAtUtc);           // the first arrival
        Assert.Equal(Utc(Mon, 12), mon.CheckOutAtUtc);         // the FIRST session's exit — what the Excel used to print
        Assert.Equal(Utc(Mon, 21, 30), mon.LastCheckOutAtUtc); // the last departure — what it prints now
        Assert.Equal(3, mon.Blocks);
        Assert.Equal(3, mon.BlockSpans!.Count);
        Assert.Equal(180 + 300 + 150, mon.WorkedMinutes);      // 10 h 30, not 12 h 30 from 09:00 to 21:30

        var tue = await Board(Tue);
        Assert.Equal(Utc(Wed, 6), tue.LastCheckOutAtUtc);      // the night's exit, the next morning
        Assert.Equal(180 + 480, tue.WorkedMinutes);
        var wed = await Board(Wed);
        Assert.Null(wed.CheckInAtUtc);                         // the night is Tuesday's, counted once
        Assert.Equal(0, wed.WorkedMinutes ?? 0);

        var thu = await Board(Thu);
        Assert.Null(thu.LastCheckOutAtUtc);                    // the last session is open: no departure to print
        Assert.Null(thu.BlockSpans![^1].OutAtUtc);
        Assert.Equal(180, thu.WorkedMinutes);                  // the open one is not counted, never estimated
        Assert.Equal("Incomplete", thu.Status);

        // The same figures the nightly summary stores — the tabel and payroll read those.
        foreach (var (date, row) in new[] { (Mon, mon), (Tue, tue), (Wed, wed), (Thu, thu) })
        {
            var stored = await h.SummaryAsync(date);
            Assert.Equal(stored!.WorkedMinutes, row.WorkedMinutes ?? 0);
        }
    }

    // ─── «Çıxışı unudulan günlər» ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_forgotten_flexible_check_out_is_never_given_a_shift_end_but_a_fixed_one_still_is()
    {
        using var h = new Harness();
        // Monday, both forgot to scan out: the flexible person came at 10:00, the fixed colleague at 09:00.
        var flexibleDay = new AttendanceRecord
        {
            TenantId = TenantId, EmployeeId = h.EmployeeId, LocationId = h.LocationId, AttendanceDate = Mon,
            CheckInAtUtc = Utc(Mon, 10), Status = AttendanceStatus.OnTime,
        };
        var fixedDay = new AttendanceRecord
        {
            TenantId = TenantId, EmployeeId = h.FixedId, LocationId = h.LocationId, AttendanceDate = Mon,
            CheckInAtUtc = Utc(Mon, 9), Status = AttendanceStatus.OnTime,
        };
        h.Db.AttendanceRecords.AddRange(flexibleDay, fixedDay);
        h.Db.SaveChanges();
        var admin = h.As(new AdminAttendanceController(h.Db, new StubSummary(), new StubQueue(), new StubPhoto(),
            NullLogger<AdminAttendanceController>.Instance, new AppOptions { TimeZone = "Asia/Baku" }));

        // The list says which is which — the screen leaves the flexible row's time empty and out of «Hamısını seç».
        var rows = Assert.IsAssignableFrom<IEnumerable<object>>(Assert.IsType<OkObjectResult>(await admin.Open()).Value).ToList();
        object Row(Guid id) => rows.Single(r => (Guid)r.GetType().GetProperty("recordId")!.GetValue(r)! == id);
        Assert.Equal(true, Row(flexibleDay.Id).GetType().GetProperty("flexible")!.GetValue(Row(flexibleDay.Id)));
        Assert.Equal(false, Row(fixedDay.Id).GetType().GetProperty("flexible")!.GetValue(Row(fixedDay.Id)));

        // The batch, sent both anyway (an older screen): the fixed day closes at its 18:00, the flexible one is left
        // open and counted apart — no exit time is invented.
        var batch = await admin.CloseOpenDays(new CloseOpenDaysRequest([flexibleDay.Id, fixedDay.Id], "Çıxışı unudub"));
        Assert.Equal(1, Field(batch, "closed"));
        Assert.Equal(1, Field(batch, "flexible"));
        Assert.Equal(0, Field(batch, "skipped"));
        Assert.Equal(Utc(Mon, 18), h.Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == fixedDay.Id).CheckOutAtUtc);
        Assert.Null(h.Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == flexibleDay.Id).CheckOutAtUtc);

        // The real time, typed by the admin, closes it — and is audited like any correction.
        var typed = await admin.Update(flexibleDay.Id, new AdminAttendanceUpdateRequest(null, Utc(Mon, 15, 30), "15:30-da getdi"));
        Assert.IsType<OkObjectResult>(typed);
        Assert.Equal(Utc(Mon, 15, 30), h.Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == flexibleDay.Id).CheckOutAtUtc);
        Assert.Contains(h.Db.AuditLogs.AsNoTracking().Where(a => a.EmployeeId == h.EmployeeId).AsEnumerable(),
            a => a.Reason != null && a.Reason.Contains("15:30-da getdi"));
    }

    // ─── stubs ──────────────────────────────────────────────────────────────────────────────────────

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

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
