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
/// «Gözlənilir» (UX-04, owner's decision 2026-11-14) — one rule on every screen that counts an absence.
///
/// At noon the tabel printed Q for people whose shift began at 14:00 while the board said «not started»,
/// and the payroll — whose range runs to today — deducted a day's pay for it. The rule now: a scheduled
/// working day with no scan is «Gözlənilir» until that day's start + the shift's late threshold has
/// passed (23:50 + 15 → 00:05 the next day, not cut at midnight). Gözlənilir is not an absence: no Q,
/// no deduction, not in any absent count — but it stays a working day, so the payroll's divisor does not
/// move. A scan, a leave, a rest day and a manager's «Qayıb yaz» keep their own meaning.
///
/// Every test pins the clock (Baku time) through TimeProvider. Dates are in March 2026 so the nightly
/// job's own "never today" guard, which reads the real clock, lets the tests write those days.
/// </summary>
public class TodayPendingReportsTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000004a4");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private static readonly AppOptions Options = new() { TimeZone = "Asia/Baku" };

    /// <summary>A Wednesday. «Today» in most tests.</summary>
    private static readonly DateOnly D11 = new(2026, 3, 11);
    private static readonly DateOnly D12 = new(2026, 3, 12);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static DateTime Utc(DateOnly date, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Baku);

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid Other { get; } = Guid.NewGuid();
        /// <summary>14:00–22:00, the branch's 15 minutes of grace.</summary>
        public Guid Afternoon { get; } = Guid.NewGuid();
        /// <summary>23:50–08:00, 15 minutes — a grace that runs past midnight.</summary>
        public Guid LateNight { get; } = Guid.NewGuid();
        /// <summary>A named shift 23:50–08:00 with 60 minutes of grace — past the 00:30 job.</summary>
        public Guid LongGrace { get; } = Guid.NewGuid();
        /// <summary>Imported, never scanned — still being set up (IsStillOnboarding).</summary>
        public Guid NeverScanned { get; } = Guid.NewGuid();
        public Guid Manager { get; } = Guid.NewGuid();

        public World()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"pending-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            foreach (var (id, name) in new[] { (Branch, "Magaza"), (Other, "Anbar") })
                Db.Locations.Add(new Location
                {
                    Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                    LateThresholdMinutes = 15, QrVersion = 1, IsActive = true, WorkDaysMask = 127,
                });

            var longGraceShift = Guid.NewGuid();
            Db.Schedules.Add(new Schedule
            {
                Id = longGraceShift, TenantId = TenantId, Name = "Gece 60",
                ShiftStart = new TimeOnly(23, 50), ShiftEnd = new TimeOnly(8, 0),
                LateThresholdMinutes = 60, WorkDaysMask = 127,
            });

            Person(Afternoon, "Gunorta", Branch, start: new TimeOnly(14, 0), end: new TimeOnly(22, 0));
            Person(LateNight, "Gece", Branch, start: new TimeOnly(23, 50), end: new TimeOnly(8, 0));
            Person(LongGrace, "Gece Uzun", Branch, scheduleId: longGraceShift);
            Person(NeverScanned, "Yeni", Branch, activated: new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc), firstScan: false);
            // The manager runs Branch but sits at Other, with a scan every day, so they never add an
            // absence of their own to the branch's numbers.
            Person(Manager, "Menecer", Other, role: EmployeeRole.Manager);
            foreach (var d in new[] { D11.AddDays(-1), D11, D12 })
                Scan(Manager, d, 8, 50, 18, 5);
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = Manager, LocationId = Branch, TenantId = TenantId });
            Db.SaveChanges();
        }

        private void Person(
            Guid id, string name, Guid location, TimeOnly? start = null, TimeOnly? end = null, Guid? scheduleId = null,
            EmployeeRole role = EmployeeRole.Employee, DateTime? activated = null, bool firstScan = true)
        {
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true, PasswordHash = "h",
                LocationId = location, WorkStart = start, WorkEnd = end, ScheduleId = scheduleId,
                MonthlySalary = 3100m,
                ActivatedAtUtc = activated ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            // One old, closed day ends the onboarding window: from then on a missing day is judged.
            if (firstScan)
                Scan(id, new DateOnly(2026, 3, 2), 9, 0, 17, 0);
        }

        public void Scan(Guid employee, DateOnly date, int inH, int inM, int? outH = null, int? outM = null, bool manual = false)
        {
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employee,
                LocationId = Db.Employees.Local.First(e => e.Id == employee).LocationId,
                AttendanceDate = date,
                CheckInAtUtc = Utc(date, inH, inM),
                CheckOutAtUtc = outH is int h ? Utc(h < inH ? date.AddDays(1) : date, h, outM ?? 0) : null,
                ManualByEmployeeId = manual ? Manager : null,
            });
            Db.SaveChanges();
        }

        public void Leave(Guid employee, DateOnly date, LeaveType type)
        {
            Db.LeaveRecords.Add(new LeaveRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employee,
                FromDate = date, ToDate = date, Type = type, CreatedByEmployeeId = Manager,
            });
            Db.SaveChanges();
        }

        public void Mark(Guid employee, DateOnly date)
        {
            Db.AbsenceMarks.Add(new AbsenceMark { TenantId = TenantId, EmployeeId = employee, Date = date, CreatedByEmployeeId = Manager });
            Db.SaveChanges();
        }

        /// <summary>What the 00:30 job does for a finished day.</summary>
        public Task RunNightlyJob(DateOnly date) => new DailySummaryService(Db, Options).GenerateForDateAsync(date);

        public ReportQueryService At(DateOnly date, int hour, int minute) =>
            new(Db, Options, new FixedClock(Utc(date, hour, minute)));

        public async Task<TabelRow> Tabel(ReportQueryService r, Guid employee, Guid? requester = null, EmployeeRole role = EmployeeRole.Admin)
        {
            var (access, report) = await r.GetTabelAsync(2026, 3, null, requester ?? Afternoon, role);
            Assert.Equal(ReportAccess.Allowed, access);
            return report!.Rows.Single(x => x.EmployeeId == employee);
        }

        public async Task<EmployeeReportRow> Summary(ReportQueryService r, Guid employee, DateOnly from, DateOnly to)
        {
            var (_, report) = await r.GetSummaryAsync(from, to, null, Afternoon, EmployeeRole.Admin);
            return report!.Rows.Single(x => x.EmployeeId == employee);
        }

        public async Task<PayrollRow> Payroll(ReportQueryService r, Guid employee, DateOnly from, DateOnly to)
        {
            var (_, report) = await r.GetPayrollAsync(from, to, null, Afternoon, EmployeeRole.Admin);
            return report!.Rows.Single(x => x.EmployeeId == employee);
        }

        public void Dispose() => Db.Dispose();
    }

    private static string Code(TabelRow row, DateOnly date) => row.Days[date.Day - 1];

    // ─── Before the shift: «Gözlənilir» on every screen ──────────────────────────────────────────────

    [Fact]
    public async Task Before_its_start_an_afternoon_shift_is_Gozlenilir_on_every_screen()
    {
        using var w = new World();
        var r = w.At(D11, 12, 0);

        var tabel = await w.Tabel(r, w.Afternoon);
        Assert.Equal("G", Code(tabel, D11));

        var summary = await w.Summary(r, w.Afternoon, D11, D11);
        Assert.Equal(0, summary.AbsentDays);
        Assert.Equal(1, summary.PendingDays);

        var pay = await w.Payroll(r, w.Afternoon, D11, D11);
        Assert.Equal(0, pay.AbsentDays);
        Assert.Equal(0m, pay.Deduction);
        Assert.Equal(1, pay.ScheduledDays); // still a working day

        var (_, dash) = await r.GetDashboardAsync(D11, D11, w.Branch, w.Afternoon, EmployeeRole.Admin);
        Assert.Equal(0, dash!.AbsentCount); // both branch people are not due yet

        var (_, days) = await r.GetEmployeeDaysAsync(w.Afternoon, D11, D11, w.Afternoon, EmployeeRole.Admin);
        Assert.Equal("Pending", Assert.Single(days).Status);

        var (_, hr) = await r.GetHrStatsAsync(new DateOnly(2026, 3, 1), D11, null, w.Afternoon, EmployeeRole.Admin);
        Assert.Equal("pending", hr!.Employees.Single(e => e.EmployeeId == w.Afternoon).Today);

        var board = await r.GetTodayAttendanceAsync(w.Afternoon, EmployeeRole.Admin);
        Assert.Equal("Pending", board.Single(x => x.EmployeeId == w.Afternoon).Status);
    }

    [Theory]
    [InlineData(13, 59, "G")]
    [InlineData(14, 0, "G")]   // the start itself
    [InlineData(14, 15, "G")]  // the last minute of the grace is still grace
    [InlineData(14, 16, "Q")]  // the grace is over and nobody came
    [InlineData(23, 0, "Q")]   // after the shift ended
    public async Task The_day_turns_Qayib_only_when_start_plus_grace_has_passed(int hour, int minute, string expected)
    {
        using var w = new World();
        var r = w.At(D11, hour, minute);

        Assert.Equal(expected, Code(await w.Tabel(r, w.Afternoon), D11));
        var summary = await w.Summary(r, w.Afternoon, D11, D11);
        var pay = await w.Payroll(r, w.Afternoon, D11, D11);
        var board = await r.GetTodayAttendanceAsync(w.Afternoon, EmployeeRole.Admin);
        if (expected == "G")
        {
            Assert.Equal((0, 1), (summary.AbsentDays, summary.PendingDays));
            Assert.Equal(0m, pay.Deduction);
            Assert.Equal("Pending", board.Single(x => x.EmployeeId == w.Afternoon).Status);
        }
        else
        {
            Assert.Equal((1, 0), (summary.AbsentDays, summary.PendingDays));
            Assert.True(pay.Deduction > 0m);
            Assert.Equal("Absent", board.Single(x => x.EmployeeId == w.Afternoon).Status);
        }
    }

    [Fact]
    public async Task The_payrolls_working_days_and_daily_rate_do_not_move_when_Gozlenilir_turns_Qayib()
    {
        using var w = new World();
        // A real absence yesterday, read from the stored day — so only TODAY changes between the two reads.
        await w.RunNightlyJob(D11.AddDays(-1));

        var before = await w.Payroll(w.At(D11, 12, 0), w.Afternoon, D11.AddDays(-1), D11);
        var after = await w.Payroll(w.At(D11, 14, 16), w.Afternoon, D11.AddDays(-1), D11);

        Assert.Equal(2, before.ScheduledDays);
        Assert.Equal(before.ScheduledDays, after.ScheduledDays);
        Assert.Equal(before.PerDay, after.PerDay);
        Assert.Equal(1, before.AbsentDays);
        Assert.Equal(2, after.AbsentDays);
        Assert.Equal(before.PerDay, after.Deduction - before.Deduction); // exactly one more day, no more
    }

    // ─── What keeps its own meaning ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(LeaveType.Vacation, "M")]
    [InlineData(LeaveType.Sick, "X")]
    [InlineData(LeaveType.Unpaid, "ÖM")]
    [InlineData(LeaveType.Permission, "İC")]
    [InlineData(LeaveType.BusinessTrip, "Ez")]
    [InlineData(LeaveType.Rest, "İs")]
    public async Task A_leave_keeps_its_own_code_before_the_shift_too(LeaveType type, string expected)
    {
        using var w = new World();
        w.Leave(w.Afternoon, D11, type);
        var r = w.At(D11, 12, 0);

        Assert.Equal(expected, Code(await w.Tabel(r, w.Afternoon), D11));
        var summary = await w.Summary(r, w.Afternoon, D11, D11);
        Assert.Equal((0, 0), (summary.AbsentDays, summary.PendingDays));
    }

    [Fact]
    public async Task A_holiday_is_B_and_never_Gozlenilir()
    {
        using var w = new World();
        w.Db.NonWorkingDays.Add(new NonWorkingDay { TenantId = TenantId, Date = D11, Description = "Bayram" });
        w.Db.SaveChanges();

        Assert.Equal("B", Code(await w.Tabel(w.At(D11, 12, 0), w.Afternoon), D11));
    }

    [Fact]
    public async Task A_scan_or_a_hand_written_record_is_worked()
    {
        using var w = new World();
        w.Scan(w.Afternoon, D11, 13, 55);                // at work, no check-out yet
        w.Scan(w.LateNight, D11, 11, 0, 19, 0, manual: true); // written by a manager
        var r = w.At(D11, 14, 30);

        Assert.Equal("İ", Code(await w.Tabel(r, w.Afternoon), D11));
        Assert.Equal("İ", Code(await w.Tabel(r, w.LateNight), D11));
    }

    [Fact]
    public async Task A_managers_Qayib_yaz_is_Q_at_once_even_before_the_shift()
    {
        using var w = new World();
        w.Mark(w.Afternoon, D11);
        var r = w.At(D11, 12, 0);

        Assert.Equal("Q", Code(await w.Tabel(r, w.Afternoon), D11));
        var summary = await w.Summary(r, w.Afternoon, D11, D11);
        Assert.Equal((1, 0), (summary.AbsentDays, summary.PendingDays));
        Assert.True((await w.Payroll(r, w.Afternoon, D11, D11)).Deduction > 0m);
        var board = await r.GetTodayAttendanceAsync(w.Afternoon, EmployeeRole.Admin);
        Assert.Equal("Absent", board.Single(x => x.EmployeeId == w.Afternoon).Status);
    }

    [Fact]
    public async Task A_mark_on_somebody_who_never_scanned_shows_Q_in_the_tabel_as_the_payroll_already_charges()
    {
        using var w = new World();
        var marked = new DateOnly(2026, 3, 5);
        w.Mark(w.NeverScanned, marked);
        await w.RunNightlyJob(marked);
        await w.RunNightlyJob(marked.AddDays(1));
        var r = w.At(D11, 12, 0);

        var tabel = await w.Tabel(r, w.NeverScanned);
        Assert.Equal("Q", Code(tabel, marked));               // was «–» while the payroll deducted it
        Assert.Equal("–", Code(tabel, marked.AddDays(1)));    // an unmarked day stays «not set up yet»
        Assert.Equal(1, (await w.Payroll(r, w.NeverScanned, marked, marked.AddDays(1))).AbsentDays);
    }

    [Fact]
    public async Task A_mark_today_on_somebody_who_never_scanned_is_Q_today_as_tonight()
    {
        using var w = new World();
        w.Mark(w.NeverScanned, D11);
        var r = w.At(D11, 8, 0);

        Assert.Equal("Q", Code(await w.Tabel(r, w.NeverScanned), D11));
        Assert.Equal(1, (await w.Summary(r, w.NeverScanned, D11, D11)).AbsentDays);
    }

    // ─── Night shifts and the change of date ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 3, "G")]
    [InlineData(0, 5, "G")]
    [InlineData(0, 6, "Q")]
    public async Task A_late_night_grace_runs_past_midnight_for_yesterday_only(int hour, int minute, string expected)
    {
        using var w = new World();
        // 00:0x on the 12th: the 11th's 23:50 shift is due at 00:05. The nightly job has not run yet.
        var r = w.At(D12, hour, minute);

        var tabel = await w.Tabel(r, w.LateNight);
        Assert.Equal(expected, Code(tabel, D11));
        Assert.Equal("G", Code(tabel, D12));                 // tonight's shift is hours away
        Assert.NotEqual("G", Code(tabel, D11.AddDays(-1)));  // two days ago is finished, whatever the hour

        var summary = await w.Summary(r, w.LateNight, D11, D11);
        Assert.Equal(expected == "G" ? (0, 1) : (1, 0), (summary.AbsentDays, summary.PendingDays));
    }

    [Fact]
    public async Task Between_midnight_and_the_0030_job_yesterday_is_computed_not_guessed()
    {
        using var w = new World();
        w.Scan(w.Afternoon, D11, 14, 0, 22, 5);   // worked the 11th
        // 00:20 on the 12th, the job has not written the 11th: the tabel used to fall back to the
        // calendar here and print Q for everybody, this man included.
        var r = w.At(D12, 0, 20);

        Assert.Equal("İ", Code(await w.Tabel(r, w.Afternoon), D11));
        Assert.Equal("Q", Code(await w.Tabel(r, w.LateNight), D11));  // its 00:05 has passed
        Assert.Equal(1, (await w.Summary(r, w.Afternoon, D11, D11)).WorkDays);

        // After the job: the same answers, now from the table.
        await w.RunNightlyJob(D11);
        var after = w.At(D12, 0, 31);
        Assert.Equal("İ", Code(await w.Tabel(after, w.Afternoon), D11));
        Assert.Equal("Q", Code(await w.Tabel(after, w.LateNight), D11));
    }

    [Theory]
    [InlineData(0, 40, "G")]  // the job wrote Absent at 00:30, but this shift's grace runs to 00:50
    [InlineData(0, 50, "G")]
    [InlineData(0, 51, "Q")]
    public async Task A_grace_longer_than_the_job_is_still_Gozlenilir_after_the_job_has_run(int hour, int minute, string expected)
    {
        using var w = new World();
        await w.RunNightlyJob(D11);
        var r = w.At(D12, hour, minute);

        Assert.Equal(expected, Code(await w.Tabel(r, w.LongGrace), D11));
        Assert.Equal(expected == "G" ? 0m : 3100m / 1, (await w.Payroll(r, w.LongGrace, D11, D11)).Deduction);
    }

    // ─── Scope and one rule ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_branch_manager_sees_the_same_Gozlenilir_and_nothing_outside_their_branches()
    {
        using var w = new World();
        var r = w.At(D11, 12, 0);

        var row = await w.Tabel(r, w.Afternoon, requester: w.Manager, role: EmployeeRole.Manager);
        Assert.Equal("G", Code(row, D11));

        var (forbidden, _) = await r.GetTabelAsync(2026, 3, w.Other, w.Manager, EmployeeRole.Manager);
        Assert.Equal(ReportAccess.Forbidden, forbidden);
    }

    [Fact]
    public async Task The_tabel_says_G_for_exactly_the_people_the_board_says_Pending()
    {
        using var w = new World();
        w.Scan(w.LateNight, D11, 9, 0); // someone at work, for contrast
        foreach (var (h, m) in new[] { (12, 0), (14, 16), (23, 55) })
        {
            var r = w.At(D11, h, m);
            var (_, tabel) = await r.GetTabelAsync(2026, 3, w.Branch, w.Afternoon, EmployeeRole.Admin);
            var board = await r.GetTodayAttendanceAsync(w.Afternoon, EmployeeRole.Admin);

            var g = tabel!.Rows.Where(x => Code(x, D11) == "G").Select(x => x.EmployeeId).ToHashSet();
            var pending = board.Where(x => x.Status == "Pending" && x.LocationId == w.Branch).Select(x => x.EmployeeId).ToHashSet();
            Assert.Equal(pending, g);
        }
    }
}
