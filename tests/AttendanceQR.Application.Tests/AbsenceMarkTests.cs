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
/// «Qayıb yaz» — absence stated by a person instead of inferred from a silence.
///
/// The pair this belongs to: <see cref="AttendanceCalculator.IsStillOnboarding"/> no longer expires,
/// so somebody who has never recorded any attendance is never automatically marked away. That closed
/// a real leak — on 2026-09-08 ten active employees who had never scanned once were carrying 28 to 45
/// Qayıb days each, and payroll deducts a day's pay per Qayıb — and it opened an obvious hole, which
/// this fills: the manager who watched somebody not turn up says so.
///
/// The rule being pinned here is the order of precedence. A scan is evidence. An approved leave is a
/// decision. A mark is testimony, and testimony loses to both.
/// </summary>
public class AbsenceMarkTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public DailySummaryService Service { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        /// <summary>Yesterday — GenerateForDateAsync refuses today and the future by design.</summary>
        public DateOnly Day { get; }

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"absence-{Guid.NewGuid()}").Options,
                tenant);
            Day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Baku)).AddDays(-1);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Dədə Qorqud Parkı",
                Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            // Imported a month ago and never once scanned — Kərimova Arzu's shape exactly.
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Kərimova Arzu", LocationId = LocationId,
                Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x",
                ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            Db.SaveChanges();

            Service = new DailySummaryService(Db, new AppOptions { TimeZone = "Asia/Baku" });
        }

        public void Mark(string? note = null)
        {
            Db.AbsenceMarks.Add(new AbsenceMark
            {
                TenantId = TenantId, EmployeeId = EmployeeId, Date = Day, Note = note,
            });
            Db.SaveChanges();
        }

        public void AddLeave(LeaveType type)
        {
            Db.LeaveRecords.Add(new LeaveRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId,
                FromDate = Day, ToDate = Day, Type = type,
            });
            Db.SaveChanges();
        }

        public void AddScan()
        {
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LocationId = LocationId,
                AttendanceDate = Day,
                CheckInAtUtc = DateTime.UtcNow.AddDays(-1).AddHours(-8),
                CheckOutAtUtc = DateTime.UtcNow.AddDays(-1),
            });
            Db.SaveChanges();
        }

        public async Task<DailySummary?> RunAsync()
        {
            await Service.GenerateForDateAsync(Day);
            Db.ChangeTracker.Clear();
            return await Db.DailySummaries.AsNoTracking()
                .FirstOrDefaultAsync(s => s.EmployeeId == EmployeeId && s.SummaryDate == Day);
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Nobody_who_has_never_scanned_is_marked_away_on_their_own()
    {
        // The leak this closed. Activated a month ago, never scanned, and no longer written up as
        // absent — the system has no evidence they were ever handed a working way to scan.
        using var h = new Harness();

        Assert.Null(await h.RunAsync());
    }

    [Fact]
    public async Task A_manager_saying_so_is_what_makes_it_a_Qayib()
    {
        using var h = new Harness();
        h.Mark("işə gəlmədi");

        var s = await h.RunAsync();

        Assert.NotNull(s);
        Assert.Equal(DailySummaryStatus.Absent, s!.Status);
        Assert.Equal(0, s.WorkedMinutes);
    }

    [Fact]
    public async Task A_scan_beats_the_mark_because_a_scan_is_evidence()
    {
        // They were marked away and then turned out to have scanned. The scan wins: the mark is one
        // person's recollection, and this is the turnstile.
        using var h = new Harness();
        h.Mark();
        h.AddScan();

        var s = await h.RunAsync();

        Assert.NotNull(s);
        Assert.NotEqual(DailySummaryStatus.Absent, s!.Status);
    }

    [Fact]
    public async Task An_approved_leave_beats_the_mark_too()
    {
        // Marked away in the morning, the holiday approved in the afternoon. The leave is the later
        // and the more considered decision, and it is the one that does not cost the person a day.
        using var h = new Harness();
        h.Mark();
        h.AddLeave(LeaveType.Vacation);

        var s = await h.RunAsync();

        Assert.NotNull(s);
        Assert.Equal(DailySummaryStatus.OnLeave, s!.Status);
    }

    [Fact]
    public async Task An_approved_leave_is_written_even_for_somebody_who_has_never_scanned()
    {
        // The hole this closed. The onboarding rule skips the day entirely for a person with no
        // attendance history — and it was skipping their APPROVED LEAVE with it. Sixteen people at
        // Bakı Abadlıq Xidməti had holidays, sick leave and a work trip entered by their managers and
        // not one row in the tabel to show for any of it. A silence may not be read as absence; a
        // decision somebody recorded may not be thrown away either.
        using var h = new Harness();
        h.AddLeave(LeaveType.Sick);

        var s = await h.RunAsync();

        Assert.NotNull(s);
        Assert.Equal(DailySummaryStatus.OnLeave, s!.Status);
    }

    [Fact]
    public async Task Taking_the_mark_off_takes_the_Qayib_off()
    {
        // It has to be undoable in the thing that reads it, not just in the table it is stored in —
        // the day was already written up as absent when the mark was made.
        using var h = new Harness();
        h.Mark();
        Assert.Equal(DailySummaryStatus.Absent, (await h.RunAsync())!.Status);

        h.Db.AbsenceMarks.RemoveRange(h.Db.AbsenceMarks);
        h.Db.SaveChanges();

        Assert.Null(await h.RunAsync());
    }
}
