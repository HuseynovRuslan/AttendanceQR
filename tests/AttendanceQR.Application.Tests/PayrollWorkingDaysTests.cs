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
/// Which days the payroll counts as WORKING days — the divisor of the daily rate, and the days a deduction
/// may fall on — end to end through <see cref="ReportQueryService.GetPayrollAsync"/>.
///
/// The rule the payroll has to follow is the one the board, the tabel and the nightly summary already
/// follow: a day is a working day when the shift ShiftOverrideMap.ScheduleFor hands back for that person on
/// that date says so (EffectiveShift.IsWorkingDay), and it is not a holiday at the day's branch. Every test
/// here is a case where the payroll used to decide it some other way and took the wrong money.
///
/// The month is November 2026 (1 November is a Sunday). The days are seeded as DailySummaries — exactly the
/// rows the nightly job writes for them — and the clock is pinned to 10 December, so the whole month is read
/// from the table and nothing depends on when the suite runs.
/// </summary>
public class PayrollWorkingDaysTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000a1d2");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 30);

    /// <summary>Monday to Friday — Sunday is bit 0, Saturday bit 6.</summary>
    private const int MonFri = 0b0111110;
    private const int EveryDay = 0b1111111;

    private static DateOnly Nov(int day) => new(2026, 11, day);

    private static bool IsWeekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public ReportQueryService Reports { get; }
        /// <summary>A Monday-to-Friday branch.</summary>
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"payroll-days-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Ofis", Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true, WorkDaysMask = MonFri,
            });
            // The admin asking has no days of their own, so they never add a line to the report.
            Db.Employees.Add(new Employee
            {
                Id = AdminId, TenantId = TenantId, FullName = "Admin", Role = EmployeeRole.Admin, IsActive = true,
                PasswordHash = "h", LocationId = Branch,
                ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            Db.SaveChanges();

            var tenth = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 12, 10, 12, 0, 0), Baku);
            Reports = new ReportQueryService(Db, new AppOptions { TimeZone = "Asia/Baku" }, new FixedClock(tenth));
        }

        public Guid Shift(string name, int mask, bool rest = false)
        {
            var id = Guid.NewGuid();
            Db.Schedules.Add(new Schedule
            {
                Id = id, TenantId = TenantId, Name = name,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = mask, IsRestDay = rest,
            });
            Db.SaveChanges();
            return id;
        }

        public Guid Person(string name, decimal salary, Guid? scheduleId = null)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = name, Role = EmployeeRole.Employee, IsActive = true,
                PasswordHash = "h", LocationId = Branch, MonthlySalary = salary, ScheduleId = scheduleId,
                ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            });
            Db.SaveChanges();
            return id;
        }

        /// <summary>The whole month as the nightly job wrote it: one row per day, null for no row.</summary>
        public void Month(Guid employee, Func<DateOnly, DailySummaryStatus?> statusOn)
        {
            for (var d = From; d <= To; d = d.AddDays(1))
            {
                if (statusOn(d) is not { } status) continue;
                var worked = status is DailySummaryStatus.OnTime or DailySummaryStatus.Late;
                Db.DailySummaries.Add(new DailySummary
                {
                    TenantId = TenantId, EmployeeId = employee, LocationId = Branch, SummaryDate = d,
                    Status = status, WorkedMinutes = worked ? 480 : 0,
                    CheckInAtUtc = worked ? TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(new TimeOnly(9, 0)), Baku) : null,
                    CheckOutAtUtc = worked ? TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(new TimeOnly(17, 0)), Baku) : null,
                });
            }
            Db.SaveChanges();
        }

        public void Leave(Guid employee, DateOnly from, DateOnly to, LeaveType type)
        {
            Db.LeaveRecords.Add(new LeaveRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employee,
                FromDate = from, ToDate = to, Type = type, CreatedByEmployeeId = AdminId,
            });
            Db.SaveChanges();
        }

        public async Task<PayrollRow> PayrollOf(Guid employee)
        {
            var (access, report) = await Reports.GetPayrollAsync(From, To, null, AdminId, EmployeeRole.Admin);
            Assert.Equal(ReportAccess.Allowed, access);
            return Assert.Single(report!.Rows, r => r.EmployeeId == employee);
        }

        public void Dispose() => Db.Dispose();
    }

    // ─── P3: the payroll asks the summary's resolver, not a copy of it ─────────────────────────────

    [Fact]
    public async Task A_seven_day_shift_at_a_weekday_branch_keeps_its_weekends_as_working_days()
    {
        // The audit's case. A named shift that works every day, at a branch whose own week is Monday to
        // Friday. The summary judged every day of November by the SHIFT — so the Saturdays and Sundays inside
        // a fortnight's holiday were holiday on working days, not days off. The payroll judged them by the
        // BRANCH's mask, took four of them out of the divisor, and charged each absence 900/26 = 34.62.
        //
        // 15 worked + 1 absent + 14 vacation = 30 working days; 900/30 = 30 an absence.
        using var h = new Harness();
        var everyDay = h.Shift("Hər gün", EveryDay);
        var id = h.Person("Yeddi Gün", 900m, everyDay);
        h.Leave(id, Nov(16), Nov(29), LeaveType.Vacation);
        h.Month(id, d =>
            d >= Nov(16) && d <= Nov(29) ? DailySummaryStatus.OnLeave
            : d == Nov(11) ? DailySummaryStatus.Absent
            : DailySummaryStatus.OnTime);

        var row = await h.PayrollOf(id);

        Assert.Equal(15, row.WorkDays);
        Assert.Equal(1, row.AbsentDays);
        Assert.Equal(14, row.VacationDays);
        Assert.Equal(30, row.ScheduledDays);
        Assert.Equal(30.00m, row.PerDay);
        Assert.Equal(30.00m, row.Deduction);
        Assert.Equal(870.00m, row.Payable);
    }

    [Fact]
    public async Task A_weekday_worker_on_the_same_holiday_still_loses_the_weekends_from_the_divisor()
    {
        // The control: the fix above must not have stopped taking real days off out of the divisor. The same
        // fortnight for somebody whose own week IS Monday to Friday holds four weekend days that were never
        // working days. 10 worked + 1 absent + 14 vacation − 4 = 21; 840/21 = 40.
        using var h = new Harness();
        var id = h.Person("Beş Gün", 840m);
        h.Leave(id, Nov(16), Nov(29), LeaveType.Vacation);
        h.Month(id, d =>
            d >= Nov(16) && d <= Nov(29) ? DailySummaryStatus.OnLeave
            : IsWeekend(d) ? DailySummaryStatus.DayOff
            : d == Nov(11) ? DailySummaryStatus.Absent
            : DailySummaryStatus.OnTime);

        var row = await h.PayrollOf(id);

        Assert.Equal(21, row.ScheduledDays);
        Assert.Equal(40.00m, row.PerDay);
        Assert.Equal(40.00m, row.Deduction);
        Assert.Equal(800.00m, row.Payable);
    }

    [Fact]
    public async Task A_standing_rotations_rest_steps_inside_a_holiday_are_not_working_days()
    {
        // Two days on, two off, as a standing shift (ShiftAssignment) from 1 November. The summary rests the
        // person on every «rest» step; the payroll knew nothing of standing shifts and read the branch's week
        // instead. The holiday 1–8 November covers four working steps and four rest steps — the branch's
        // week calls only three of those days (Sun 1, Sat 7, Sun 8) off.
        //
        // 9–30 November: 12 working steps (one of them absent), 10 rest steps.
        // 11 worked + 1 absent + 8 vacation − 4 rest steps = 16; 1600/16 = 100. The old count was 17 (94.12).
        using var h = new Harness();
        var day = h.Shift("Gündüz", EveryDay);
        var id = h.Person("Növbəli", 1600m);
        h.Db.ShiftAssignments.Add(new ShiftAssignment
        {
            TenantId = TenantId, EmployeeId = id, From = Nov(1), Steps = $"{day},{day},rest,rest",
        });
        h.Db.SaveChanges();
        h.Leave(id, Nov(1), Nov(8), LeaveType.Vacation);
        bool Working(DateOnly d) => (d.DayNumber - Nov(1).DayNumber) % 4 < 2;
        h.Month(id, d =>
            d <= Nov(8) ? DailySummaryStatus.OnLeave
            : !Working(d) ? DailySummaryStatus.DayOff
            : d == Nov(10) ? DailySummaryStatus.Absent
            : DailySummaryStatus.OnTime);

        var row = await h.PayrollOf(id);

        Assert.Equal(11, row.WorkDays);
        Assert.Equal(16, row.ScheduledDays);
        Assert.Equal(100.00m, row.PerDay);
        Assert.Equal(100.00m, row.Deduction);
    }

    [Fact]
    public async Task A_planned_rest_day_inside_a_holiday_is_not_a_working_day()
    {
        // «İstirahət» planned on Wednesday 18 November (/admin/schedules), then a holiday filed over the whole
        // week. The leave beats the day off on the board, as it should, so the 18th arrives as a leave day —
        // but it was not a working day, and the payroll counted it as one.
        //
        // 21 weekdays − the planned rest = 20; 1000/20 = 50 for the one absence (not 1000/21 = 47.62).
        using var h = new Harness();
        var rest = h.Shift("İstirahət", 0, rest: true);
        var id = h.Person("Planlı", 1000m);
        h.Db.ShiftOverrides.Add(new ShiftOverride { TenantId = TenantId, EmployeeId = id, Date = Nov(18), ScheduleId = rest });
        h.Db.SaveChanges();
        h.Leave(id, Nov(16), Nov(20), LeaveType.Vacation);
        h.Month(id, d =>
            IsWeekend(d) ? DailySummaryStatus.DayOff
            : d >= Nov(16) && d <= Nov(20) ? DailySummaryStatus.OnLeave
            : d == Nov(4) ? DailySummaryStatus.Absent
            : DailySummaryStatus.OnTime);

        var row = await h.PayrollOf(id);

        Assert.Equal(20, row.ScheduledDays);
        Assert.Equal(50.00m, row.PerDay);
        Assert.Equal(50.00m, row.Deduction);
    }

    [Fact]
    public async Task A_shift_edited_mid_month_judges_the_days_before_the_edit_by_the_old_hours()
    {
        // «Növbə» worked every day until 15 November and was then edited to Monday–Friday; the edit kept the
        // old version as a revision (ValidUntil 15 November), so the summary judged 1–15 November as seven-day
        // weeks. A holiday over 2–15 November therefore covers fourteen WORKING days. Asking the shift as it is
        // now — or the branch's week, as the payroll did — calls the four weekend days in it days off.
        //
        // Sun 1 worked, 2–15 vacation, then 11 weekdays (one absent): 1 + 14 + 11 = 26; 1300/26 = 50.
        using var h = new Harness();
        var shift = h.Shift("Növbə", MonFri);
        h.Db.Schedules.Add(new Schedule
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Name = "Növbə",
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, WorkDaysMask = EveryDay,
            RevisionOfId = shift, ValidUntil = Nov(15),
        });
        h.Db.SaveChanges();
        var id = h.Person("Dəyişən", 1300m, shift);
        h.Leave(id, Nov(2), Nov(15), LeaveType.Vacation);
        h.Month(id, d =>
            d == Nov(1) ? DailySummaryStatus.OnTime
            : d <= Nov(15) ? DailySummaryStatus.OnLeave
            : IsWeekend(d) ? DailySummaryStatus.DayOff
            : d == Nov(17) ? DailySummaryStatus.Absent
            : DailySummaryStatus.OnTime);

        var row = await h.PayrollOf(id);

        Assert.Equal(26, row.ScheduledDays);
        Assert.Equal(50.00m, row.PerDay);
        Assert.Equal(50.00m, row.Deduction);
    }

    // ─── the one place the payroll cannot ask the summary verbatim ────────────────────────────────

    private static readonly Location Weekdays = new()
    {
        ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15, WorkDaysMask = MonFri,
    };

    [Fact]
    public void A_fixed_shift_is_asked_exactly_what_the_summary_asks()
    {
        var everyDay = EffectiveShift.Resolve(null, null, null, 1, null,
            new Schedule { Name = "Hər gün", ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), WorkDaysMask = EveryDay },
            Weekdays);
        var branch = EffectiveShift.Resolve(null, null, null, 1, null, null, Weekdays);

        Assert.True(PayrollMath.IsWorkingDay(everyDay, Nov(21), isHoliday: false));    // Saturday, by the shift
        Assert.False(PayrollMath.IsWorkingDay(branch, Nov(21), isHoliday: false));     // Saturday, by the branch
        Assert.True(PayrollMath.IsWorkingDay(branch, Nov(23), isHoliday: false));      // Monday
        Assert.False(PayrollMath.IsWorkingDay(branch, Nov(23), isHoliday: true));      // a bayram is never one
    }

    [Fact]
    public void A_flexible_shift_counts_its_own_working_week()
    {
        // «Elastik qrafik» is due on no day at all (EffectiveShift.IsWorkingDay is false for every date), which
        // the payroll cannot take literally: a person with no working day has nothing deducted, so their unpaid
        // leave would be paid. It falls back to the shift's own week — the days its daily norm applies on.
        var flexible = EffectiveShift.Resolve(null, null, null, 1, null,
            new Schedule { Name = "Elastik", IsFlexible = true, WorkDaysMask = MonFri }, Weekdays);

        Assert.False(flexible.IsWorkingDay(Nov(23)));                                   // the summary's answer
        Assert.True(PayrollMath.IsWorkingDay(flexible, Nov(23), isHoliday: false));     // Monday: in its week
        Assert.False(PayrollMath.IsWorkingDay(flexible, Nov(22), isHoliday: false));    // Sunday: not
        Assert.False(PayrollMath.IsWorkingDay(flexible, Nov(23), isHoliday: true));
    }
}
