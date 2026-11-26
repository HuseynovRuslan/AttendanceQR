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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A leave filed or removed for PAST days changes what those days pay, and the stored summaries are what the tabel and
/// payroll read. Two ways that went wrong on Postgres and never in the in-memory tests:
///
/// The rebuild ran after the commit on the request's own cancellation token. Closing the tab or backgrounding the phone
/// app mid-rebuild left every day after that point on its old summary — a sick leave still read Qayıb and was deducted —
/// and nothing repaired it: a retry answered «AllOverlap» before rebuilding anything, and no job looks at those days.
/// Now the days are noted in the same save as the leave and the rebuild runs to the end (PlanRebuilds).
///
/// The note was never length-checked, while LeaveRecords.Note is varchar(500): one pasted doctor's note failed the
/// whole crew's batch with a 500 and no reason. Now it is a 400 «NoteTooLong» before anything is written.
///
/// The summary service here behaves the way Npgsql does with a cancelled token — the next query throws — so the first
/// group fails without the fix even though the store is in-memory.
/// </summary>
public class LeaveRebuildDurabilityTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000001e5");
    private static DateOnly Today => ScheduleRevisions.Today(ScheduleRevisions.Zone("Asia/Baku"));

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        public Guid OtherStaffId { get; } = Guid.NewGuid();
        public CancellationTokenSource Request { get; } = new();
        public LeavingClient Summaries { get; }

        public Harness(IInterceptor? interceptor = null)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"leave-rebuild-{Guid.NewGuid()}");
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            Db = new AppDbContext(options.Options, tenant);
            Summaries = new LeavingClient(Request);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Filial", Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15,
                QrVersion = 1, IsActive = true,
            });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = Branch, TenantId = TenantId });
            Db.Employees.AddRange(
                Person(AdminId, "Admin", EmployeeRole.Admin),
                Person(ManagerId, "Menecer", EmployeeRole.Manager),
                Person(StaffId, "Birinci İşçi", EmployeeRole.Employee),
                Person(OtherStaffId, "İkinci İşçi", EmployeeRole.Employee));
            Db.SaveChanges();
        }

        private Employee Person(Guid id, string name, EmployeeRole role) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = Branch, IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow.AddYears(-1), PasswordHash = "h",
        };

        private ControllerContext As(Guid id, EmployeeRole role) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", id.ToString()), new Claim("role", role.ToString())], "test")),
                RequestAborted = Request.Token,
            },
        };

        public AdminLeaveController Admin() =>
            new(Db, Summaries, new AppOptions { TimeZone = "Asia/Baku" }) { ControllerContext = As(AdminId, EmployeeRole.Admin) };

        public AdminAttendanceController Attendance() =>
            new(Db, Summaries, new NoFaceQueue(), new NoPhotos(), NullLogger<AdminAttendanceController>.Instance,
                new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = As(AdminId, EmployeeRole.Admin),
            };

        public ManagerController Manager() =>
            new(Db, new StubHasher(), Summaries, new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = As(ManagerId, EmployeeRole.Manager),
            };

        public LeaveRecord Seed(DateOnly from, DateOnly to)
        {
            var leave = new LeaveRecord
            {
                TenantId = TenantId, EmployeeId = StaffId, FromDate = from, ToDate = to, Type = LeaveType.Sick,
                CreatedByEmployeeId = AdminId,
            };
            Db.LeaveRecords.Add(leave);
            Db.SaveChanges();
            return leave;
        }

        public List<DateOnly> Notes() => Db.PendingSummaryRebuilds.AsNoTracking().Select(n => n.Date).OrderBy(d => d).ToList();

        public void Dispose()
        {
            Request.Dispose();
            Db.Dispose();
        }
    }

    /// <summary>
    /// The nightly summary as Npgsql would run it from a request: a cancelled token throws at the next query. The
    /// person who asked closes the tab as soon as the first day is done; a day in <see cref="Failing"/> fails the way a
    /// database error would.
    /// </summary>
    private sealed class LeavingClient(CancellationTokenSource request) : IDailySummaryService
    {
        public List<DateOnly> Rebuilt { get; } = [];
        public HashSet<DateOnly> Failing { get; } = [];

        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Failing.Contains(date))
                throw new InvalidOperationException("simulated: the rebuild of this day failed");
            Rebuilt.Add(date);
            request.Cancel();
            return Task.FromResult(1);
        }
    }

    /// <summary>The person closes the tab the moment the change is committed — before any day is rebuilt.</summary>
    private sealed class LeavesAfterTheSave : SaveChangesInterceptor
    {
        public CancellationTokenSource? Request { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Request?.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NoPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url,
            CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoFaceQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private sealed class NoPhotos : IPhotoStorageService
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

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private static List<DateOnly> Days(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
            days.Add(d);
        return days;
    }

    private static LeaveRecordRequest Leave(Guid[] who, DateOnly from, DateOnly to, string? note = null) =>
        new(who[0], from, to, LeaveType.Sick, note, who);

    private static string? Error(IActionResult result) =>
        result is ObjectResult { Value: { } v } ? v.GetType().GetProperty("error")?.GetValue(v) as string : null;

    // ─── the rebuild survives the person leaving ────────────────────────────────────────────────────

    [Fact]
    public async Task A_past_leave_has_every_day_rebuilt_even_when_the_admin_closes_the_tab_after_the_first()
    {
        using var h = new Harness();
        var (from, to) = (Today.AddDays(-10), Today.AddDays(-6));

        var result = await h.Admin().Create(Leave([h.StaffId, h.OtherStaffId], from, to));

        Assert.IsType<OkObjectResult>(result);
        Assert.True(h.Request.IsCancellationRequested, "the client must have left mid-rebuild, or this proves nothing");
        Assert.Equal(Days(from, to), h.Summaries.Rebuilt);
        Assert.Empty(h.Notes());
        Assert.Equal(2, h.Db.LeaveRecords.AsNoTracking().Count());
    }

    [Fact]
    public async Task Removing_a_past_leave_rebuilds_its_days_to_the_end_too()
    {
        using var h = new Harness();
        var leave = h.Seed(Today.AddDays(-9), Today.AddDays(-7));

        Assert.IsType<OkObjectResult>(await h.Admin().Delete(leave.Id));

        Assert.Equal(Days(Today.AddDays(-9), Today.AddDays(-7)), h.Summaries.Rebuilt);
        Assert.Empty(h.Notes());
        Assert.Empty(h.Db.LeaveRecords.AsNoTracking());
    }

    [Fact]
    public async Task A_managers_leave_is_rebuilt_to_the_end_when_the_phone_app_is_backgrounded()
    {
        using var h = new Harness();
        var (from, to) = (Today.AddDays(-5), Today.AddDays(-2));

        Assert.IsType<OkObjectResult>(await h.Manager().CreateLeave(Leave([h.StaffId], from, to)));

        Assert.Equal(Days(from, to), h.Summaries.Rebuilt);
        Assert.Empty(h.Notes());
    }

    [Fact]
    public async Task A_managers_removal_of_a_past_leave_is_rebuilt_to_the_end()
    {
        using var h = new Harness();
        var leave = h.Seed(Today.AddDays(-4), Today.AddDays(-1));

        Assert.IsType<OkObjectResult>(await h.Manager().DeleteLeave(leave.Id));

        Assert.Equal(Days(Today.AddDays(-4), Today.AddDays(-1)), h.Summaries.Rebuilt);
        Assert.Empty(h.Notes());
    }

    [Fact]
    public async Task A_day_whose_rebuild_fails_keeps_its_note_so_the_worker_finishes_it()
    {
        using var h = new Harness();
        var (from, to) = (Today.AddDays(-10), Today.AddDays(-6));
        h.Summaries.Failing.Add(Today.AddDays(-8));

        // The leave is committed; the failure is the worker's to retry, not a 500 after the fact.
        Assert.IsType<OkObjectResult>(await h.Admin().Create(Leave([h.StaffId], from, to)));

        Assert.Equal([Today.AddDays(-8)], h.Notes());
        Assert.Equal(4, h.Summaries.Rebuilt.Count);
        Assert.Single(h.Db.LeaveRecords.AsNoTracking());
    }

    [Fact]
    public async Task Today_and_the_future_are_neither_noted_nor_rebuilt()
    {
        // They have no stored summary to change — today and the future are computed live.
        using var h = new Harness();

        Assert.IsType<OkObjectResult>(await h.Admin().Create(Leave([h.StaffId], Today.AddDays(-2), Today.AddDays(30))));

        Assert.Equal([Today.AddDays(-2), Today.AddDays(-1)], h.Summaries.Rebuilt);
        Assert.Empty(h.Notes());
    }

    [Fact]
    public async Task A_day_another_request_noted_a_moment_earlier_does_not_fail_the_leave()
    {
        // One note per company and day: the second insert is a 23505 on Postgres. The leave must still be saved.
        var taken = new NoteTakenOnce();
        using var h = new Harness(taken);
        var (from, to) = (Today.AddDays(-6), Today.AddDays(-4));

        Assert.IsType<OkObjectResult>(await h.Admin().Create(Leave([h.StaffId], from, to)));

        Assert.True(taken.Refused, "the stand-in index was never hit, so this test would prove nothing");
        Assert.Single(h.Db.LeaveRecords.AsNoTracking());
        Assert.Equal(Days(from, to), h.Summaries.Rebuilt);
    }

    // ─── the note fits its column ───────────────────────────────────────────────────────────────────

    [Fact]
    public void The_limit_checked_is_the_columns_own()
    {
        using var h = new Harness();
        var column = h.Db.Model.FindEntityType(typeof(LeaveRecord))!.FindProperty(nameof(LeaveRecord.Note))!.GetMaxLength();
        Assert.Equal(LeaveNote.MaxLength, column);
    }

    [Fact]
    public async Task An_admins_note_longer_than_the_column_is_refused_before_anything_is_written()
    {
        using var h = new Harness();

        var result = await h.Admin().Create(
            Leave([h.StaffId, h.OtherStaffId], Today.AddDays(-3), Today.AddDays(-3), new string('x', LeaveNote.MaxLength + 1)));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("NoteTooLong", Error(result));
        Assert.Empty(h.Db.LeaveRecords.AsNoTracking());
        Assert.Empty(h.Summaries.Rebuilt);
    }

    [Fact]
    public async Task A_managers_note_longer_than_the_column_is_refused_before_anything_is_written()
    {
        using var h = new Harness();

        var result = await h.Manager().CreateLeave(
            Leave([h.StaffId], Today.AddDays(-3), Today.AddDays(-3), new string('ə', LeaveNote.MaxLength + 1)));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("NoteTooLong", Error(result));
        Assert.Empty(h.Db.LeaveRecords.AsNoTracking());
    }

    [Fact]
    public async Task A_note_at_the_limit_is_kept_whole_and_every_row_of_the_batch_fits_the_column()
    {
        using var h = new Harness();
        var max = h.Db.Model.FindEntityType(typeof(LeaveRecord))!.FindProperty(nameof(LeaveRecord.Note))!.GetMaxLength()!.Value;
        // Padding is trimmed before the length is judged — the column stores the trimmed note. (Future dates, so no
        // rebuild runs and the stand-in client never leaves.)
        var note = "  " + new string('ə', max) + "  ";
        var day = Today.AddDays(3);

        Assert.IsType<OkObjectResult>(await h.Admin().Create(Leave([h.StaffId, h.OtherStaffId], day, day, note)));
        Assert.IsType<OkObjectResult>(await h.Manager().CreateLeave(Leave([h.ManagerId], day, day, note)));

        var stored = h.Db.LeaveRecords.AsNoTracking().Select(l => l.Note).ToList();
        Assert.Equal(3, stored.Count);
        Assert.All(stored, n => Assert.Equal(max, n!.Length));
    }

    // ─── the same pattern outside leaves ────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_void_of_a_past_day_is_rebuilt_even_when_the_admin_leaves_right_after_the_save()
    {
        // Every post-commit rebuild that ran on the request's token moved to the same pattern; the void is the one
        // whose stale day kept paying for a fraudulent scan.
        var leaves = new LeavesAfterTheSave();
        using var h = new Harness(leaves);
        var day = Today.AddDays(-3);
        var record = new AttendanceRecord
        {
            TenantId = TenantId, EmployeeId = h.StaffId, LocationId = h.Branch, AttendanceDate = day,
            CheckInAtUtc = DateTime.UtcNow.AddDays(-3), CheckOutAtUtc = DateTime.UtcNow.AddDays(-3).AddHours(8),
            Status = AttendanceStatus.OnTime,
        };
        h.Db.AttendanceRecords.Add(record);
        h.Db.SaveChanges();
        leaves.Request = h.Request;

        var result = await h.Attendance().VoidFraud(record.Id, new VoidFraudRequest("Saxta şəkil", NotifyEmployee: false), new NoPush());

        Assert.IsType<OkObjectResult>(result);
        Assert.True(h.Request.IsCancellationRequested);
        Assert.Equal([day], h.Summaries.Rebuilt);
        Assert.Empty(h.Notes());
        Assert.NotNull(h.Db.AttendanceRecords.AsNoTracking().Single().VoidedAtUtc);
    }
}
