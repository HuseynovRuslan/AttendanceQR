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
/// The «İş qrafiki» column of the redesigned board — the hours a row is being judged against.
///
/// It is sent from the server rather than looked up again on the client, and that is the whole point:
/// the status beside it («Qayıb», «Növbəsi başlamayıb») is decided from one resolved
/// <see cref="EffectiveShift"/>, and a column that re-derived the window from the location would
/// disagree with the badge next to it on exactly the people whose hours are unusual — a night guard,
/// a crew whose Saturday starts later, the split-shift «əlavə qüvvə» team. Those three are the ones an
/// admin opens this column to check.
/// </summary>
public class TodayBoardScheduleTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b7");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public DateOnly Today { get; }

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"today-sched-{Guid.NewGuid()}").Options,
                tenant);
            Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku));

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Baş ofis",
                Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
                // The fallback window: what a person with no schedule and no personal hours is judged by.
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Qrafik İşçisi", LocationId = LocationId,
                Role = EmployeeRole.Admin, IsActive = true, PasswordHash = "x",
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            // One real day behind them: somebody who has never recorded any attendance is never judged
            // at all, and would arrive as «Aktivləşdirməyib» rather than as a scheduled person.
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LocationId = LocationId,
                AttendanceDate = Today.AddDays(-20),
                CheckInAtUtc = DateTime.UtcNow.AddDays(-20),
                CheckOutAtUtc = DateTime.UtcNow.AddDays(-20).AddHours(8),
            });
            Db.SaveChanges();
        }

        /// <summary>Puts the employee on a named shift and returns the board's single row.</summary>
        public async Task<DayAttendanceRow> RowAsync(Schedule? schedule = null)
        {
            if (schedule is not null)
            {
                schedule.Id = Guid.NewGuid();
                schedule.TenantId = TenantId;
                Db.Schedules.Add(schedule);
                var e = Db.Employees.Single(x => x.Id == EmployeeId);
                e.ScheduleId = schedule.Id;
                await Db.SaveChangesAsync();
            }
            var reports = new ReportQueryService(Db, new AppOptions { TimeZone = "Asia/Baku" });
            var board = await reports.GetTodayAttendanceAsync(EmployeeId, EmployeeRole.Admin);
            return Assert.Single(board);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task With_no_schedule_the_row_carries_the_locations_own_hours()
    {
        using var h = new Harness();

        var row = await h.RowAsync();

        Assert.Equal("09:00", row.ShiftStart);
        Assert.Equal("18:00", row.ShiftEnd);
        // No name, because no named shift decided it — the column then shows the times alone rather
        // than inventing a label the admin would go looking for in /admin/schedules.
        Assert.Null(row.ShiftName);
        Assert.Null(row.SecondShiftStart);
    }

    [Fact]
    public async Task A_named_shift_sends_its_name_as_well_as_its_hours()
    {
        using var h = new Harness();

        var row = await h.RowAsync(new Schedule
        {
            Name = "Gecə A", ShiftStart = new TimeOnly(22, 0), ShiftEnd = new TimeOnly(6, 0), WorkDaysMask = 127,
        });

        Assert.Equal("22:00", row.ShiftStart);
        Assert.Equal("06:00", row.ShiftEnd);
        Assert.Equal("Gecə A", row.ShiftName);
    }

    [Fact]
    public async Task A_day_with_its_own_hours_shows_THOSE_hours()
    {
        // Heydər Əliyev Mərkəzi: 08:00–18:00 on weekdays, 09:00–18:00 at the weekend. The board is
        // judging today against the day's own window, so the column has to print the same one — the
        // alternative is a row badged «Növbəsi başlamayıb» beside hours that say it began an hour ago.
        using var h = new Harness();
        var dow = (int)h.Today.DayOfWeek;

        var row = await h.RowAsync(new Schedule
        {
            Name = "Mərkəz", ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0), WorkDaysMask = 127,
            DayHours = $"{dow}=10:30-19:30",
        });

        Assert.Equal("10:30", row.ShiftStart);
        Assert.Equal("19:30", row.ShiftEnd);
    }

    [Fact]
    public async Task A_split_day_sends_the_second_window_too()
    {
        // «Əlavə qüvvə»: 07:00–11:00 and then 22:00–07:00. Sending the first window alone would print
        // a four-hour day for the one crew whose schedule is the thing worth reading.
        using var h = new Harness();

        var row = await h.RowAsync(new Schedule
        {
            Name = "İkiqat", ShiftStart = new TimeOnly(7, 0), ShiftEnd = new TimeOnly(11, 0), WorkDaysMask = 127,
            SecondShiftStart = new TimeOnly(22, 0), SecondShiftEnd = new TimeOnly(7, 0),
        });

        Assert.Equal("07:00", row.ShiftStart);
        Assert.Equal("11:00", row.ShiftEnd);
        Assert.Equal("22:00", row.SecondShiftStart);
        Assert.Equal("07:00", row.SecondShiftEnd);
    }

    [Fact]
    public async Task The_employees_own_hours_beat_the_location()
    {
        // Three people are still on individual hours rather than a named shift ([[shift-named-only-model]]).
        using var h = new Harness();
        var e = h.Db.Employees.Single(x => x.Id == h.EmployeeId);
        e.WorkStart = new TimeOnly(7, 30);
        e.WorkEnd = new TimeOnly(16, 30);
        h.Db.SaveChanges();

        var row = await h.RowAsync();

        Assert.Equal("07:30", row.ShiftStart);
        Assert.Equal("16:30", row.ShiftEnd);
        Assert.Null(row.ShiftName);
    }
}
