using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Application.Reporting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A day worked in two stretches, driven through the real scan endpoint.
///
/// The «əlavə qüvvə» crew washes an area from 07:00 to 11:00, goes home, and comes back at 22:00
/// until the next morning. Both stretches fall on one calendar day. Until this feature the 11:00
/// check-out closed the day and the 22:00 arrival was refused as «artıq tamamlamısınız» — nine hours
/// of night work were not mis-measured, they were never recorded.
///
/// The control test is the important one. An employee whose shift declares no second window must
/// still be refused, because that is six hundred and sixty people and the whole design rests on them
/// being unable to reach this path at all.
///
/// The second window is built around the CURRENT time rather than a fixed 22:00 on purpose: the
/// endpoint reads the wall clock, and a test that only passes in the evening is a test that fails the
/// first morning somebody runs the suite.
/// </summary>
public class SplitShiftScanTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e7");
    private const double OfficeLat = 40.4093;
    private const double OfficeLng = 49.8671;

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AttendanceController Controller { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        private readonly IQrTokenService _qr;

        /// <param name="secondWindow">Give the employee's shift a second stretch covering right now.
        /// False produces an ordinary shift — the control.</param>
        public Harness(bool secondWindow)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"split-{Guid.NewGuid()}")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Fəvvarələr",
                Latitude = OfficeLat, Longitude = OfficeLng, RadiusMeters = 150,
                ShiftStart = new TimeOnly(7, 0), ShiftEnd = new TimeOnly(17, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });

            Guid? scheduleId = null;
            if (secondWindow)
            {
                // Anchored on the clock the endpoint will read, so the test says the same thing at
                // nine in the morning as at midnight.
                var nowLocal = TimeOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku")));
                scheduleId = Guid.NewGuid();
                Db.Schedules.Add(new Schedule
                {
                    Id = scheduleId.Value, TenantId = TenantId, LocationId = LocationId,
                    Name = "Əlavə qüvvə — ikiqat gün",
                    ShiftStart = new TimeOnly(7, 0), ShiftEnd = new TimeOnly(11, 0),
                    SecondShiftStart = nowLocal.AddMinutes(-30),
                    SecondShiftEnd = nowLocal.AddHours(8),
                    WorkDaysMask = 127, LateThresholdMinutes = 15,
                });
            }

            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Əlavə Qüvvə",
                Email = "eq@baki.local", LocationId = LocationId, ScheduleId = scheduleId,
                Role = EmployeeRole.Employee, IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
            });
            Db.SaveChanges();

            _qr = new QrTokenService(Options.Create(new QrTokenOptions
            {
                Secret = "test-secret-key-for-split-shift-tests",
                TtlSeconds = 300,
            }));

            var identity = new ClaimsIdentity([new Claim("sub", EmployeeId.ToString())], "test");
            Controller = new AttendanceController(
                Db, _qr, new StubQuery(), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(),
                new StubFace(), new DeviceBindingOptions { AutoBind = true },
                new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AttendanceController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        public ScanRequest Scan() =>
            new(_qr.Generate(LocationId, 1), "device-fp-1", OfficeLat, OfficeLng,
                PhotoBase64: null, ClientScanId: null, ClientTimestampUtc: null, Offline: false);

        /// <summary>Finish the day's first stretch: check in, back-date the arrival so the check-out
        /// cool-off is satisfied, then check out.</summary>
        public async Task CompleteFirstBlockAsync()
        {
            await Controller.Scan(Scan());
            var first = await Db.AttendanceRecords.FirstAsync();
            first.CheckInAtUtc = DateTime.UtcNow.AddHours(-9);
            await Db.SaveChangesAsync();
            await Controller.Scan(Scan());
            var closed = await Db.AttendanceRecords.FirstAsync();
            closed.CheckOutAtUtc = DateTime.UtcNow.AddHours(-5);   // long enough ago to be no double-tap
            await Db.SaveChangesAsync();
        }

        public Task<List<AttendanceRecord>> BlocksAsync() =>
            Db.AttendanceRecords.OrderBy(r => r.CheckInAtUtc).ToListAsync();

        public void Dispose() => Db.Dispose();
    }

    private static string? Error(IActionResult r) =>
        r is ObjectResult { Value: { } v } ? v.GetType().GetProperty("error")?.GetValue(v) as string : null;

    // --- the control: everybody else --------------------------------------

    [Fact]
    public async Task An_ordinary_shift_still_refuses_a_scan_after_the_day_is_done()
    {
        // THE test. Six hundred and sixty people are on shifts with no second window, and the entire
        // safety of this feature is that they cannot reach the new path at all.
        using var h = new Harness(secondWindow: false);
        await h.CompleteFirstBlockAsync();

        var third = await h.Controller.Scan(h.Scan());

        Assert.Equal("AlreadyCompleted", Error(third));
        Assert.Single(await h.BlocksAsync());
    }

    // --- the crew this was built for ---------------------------------------

    [Fact]
    public async Task Coming_back_in_the_evening_opens_a_second_stretch_of_the_same_day()
    {
        using var h = new Harness(secondWindow: true);
        await h.CompleteFirstBlockAsync();

        var evening = await h.Controller.Scan(h.Scan());

        Assert.Null(Error(evening));
        var blocks = await h.BlocksAsync();
        Assert.Equal(2, blocks.Count);
        // Same calendar day, and the first stretch is left exactly as it was.
        Assert.Equal(blocks[0].AttendanceDate, blocks[1].AttendanceDate);
        Assert.NotNull(blocks[0].CheckOutAtUtc);
        Assert.Null(blocks[1].CheckOutAtUtc);
    }

    [Fact]
    public async Task The_second_stretch_is_checked_out_of_like_any_other()
    {
        using var h = new Harness(secondWindow: true);
        await h.CompleteFirstBlockAsync();
        await h.Controller.Scan(h.Scan());

        // Back-date the second arrival past the cool-off, then scan again.
        var open = await h.Db.AttendanceRecords.FirstAsync(r => r.CheckOutAtUtc == null);
        open.CheckInAtUtc = DateTime.UtcNow.AddHours(-3);
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.Scan(h.Scan());

        Assert.Null(Error(result));
        var blocks = await h.BlocksAsync();
        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.NotNull(b.CheckOutAtUtc));
    }

    [Fact]
    public async Task A_third_stretch_is_refused_even_on_a_split_shift()
    {
        // A split day is two stretches, not a season ticket. The old protection has to survive for
        // the same person on the same day.
        using var h = new Harness(secondWindow: true);
        await h.CompleteFirstBlockAsync();
        await h.Controller.Scan(h.Scan());
        var open = await h.Db.AttendanceRecords.FirstAsync(r => r.CheckOutAtUtc == null);
        open.CheckInAtUtc = DateTime.UtcNow.AddHours(-3);
        await h.Db.SaveChangesAsync();
        await h.Controller.Scan(h.Scan());   // closes the second

        var third = await h.Controller.Scan(h.Scan());

        Assert.Equal("AlreadyCompleted", Error(third));
        Assert.Equal(2, (await h.BlocksAsync()).Count);
    }

    [Fact]
    public async Task A_double_tap_right_after_the_first_check_out_does_not_open_a_stretch()
    {
        // The accident this feature must not introduce: «did it work?» two minutes after leaving.
        using var h = new Harness(secondWindow: true);
        await h.Controller.Scan(h.Scan());
        var first = await h.Db.AttendanceRecords.FirstAsync();
        first.CheckInAtUtc = DateTime.UtcNow.AddHours(-9);
        await h.Db.SaveChangesAsync();
        await h.Controller.Scan(h.Scan());   // check-out, just now

        var tap = await h.Controller.Scan(h.Scan());

        Assert.Equal("AlreadyCompleted", Error(tap));
        Assert.Single(await h.BlocksAsync());
    }

    [Fact]
    public async Task While_the_second_stretch_is_open_the_day_is_still_one_open_block()
    {
        // The invariant the database now enforces: never two open stretches at once.
        using var h = new Harness(secondWindow: true);
        await h.CompleteFirstBlockAsync();
        await h.Controller.Scan(h.Scan());

        Assert.Single(await h.Db.AttendanceRecords.Where(r => r.CheckOutAtUtc == null).ToListAsync());
    }

    // Local stubs: the controller needs its collaborators, and none of them has anything to say about
    // a second stretch of a day. Kept private to this file rather than shared — a stub reused across
    // suites becomes a second place where behaviour is decided.
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

    // --- coming back from an assignment ------------------------------------

    [Fact]
    public async Task A_day_a_field_visit_closed_can_be_reopened_at_the_poster()
    {
        // «Səhər 08:00-da ezamiyyətə getdi, 11:00-da bitirib mərkəzə qayıtdı, QR vura bilmir.» The
        // morning check-in was closed on their behalf when they left the site, so the poster read the
        // day as finished — and the rest of the day they worked at the centre was recorded nowhere.
        using var h = new Harness(secondWindow: false);   // an ORDINARY shift: no second window
        await h.CompleteFirstBlockAsync();
        var morning = await h.Db.AttendanceRecords.FirstAsync();
        morning.ClosedByFieldVisitId = Guid.NewGuid();    // what TryCloseOpenAttendanceAsync stamps
        await h.Db.SaveChangesAsync();

        var backAtTheCentre = await h.Controller.Scan(h.Scan());

        Assert.Null(Error(backAtTheCentre));
        var blocks = await h.BlocksAsync();
        Assert.Equal(2, blocks.Count);
        Assert.Equal(blocks[0].AttendanceDate, blocks[1].AttendanceDate);
        Assert.NotNull(blocks[0].CheckOutAtUtc);   // the morning stays exactly as the visit left it
        Assert.Null(blocks[1].CheckOutAtUtc);
    }

    [Fact]
    public async Task A_day_the_employee_closed_themselves_is_not_reopened()
    {
        // THE boundary. The same shift, the same finished day — the only difference is that no field
        // visit closed it. If this ever opens a block, every ordinary check-out in the company becomes
        // re-openable by the next tap, and the refusal that protects six hundred people is gone.
        using var h = new Harness(secondWindow: false);
        await h.CompleteFirstBlockAsync();

        var again = await h.Controller.Scan(h.Scan());

        Assert.Equal("AlreadyCompleted", Error(again));
        Assert.Single(await h.BlocksAsync());
    }

    [Fact]
    public async Task A_reopened_day_still_stops_at_two_stretches()
    {
        // The cap is shared with the split shift deliberately: whatever reason a day has for a second
        // stretch, a third is a retry loop, and an unbounded one writes blocks all afternoon.
        using var h = new Harness(secondWindow: false);
        await h.CompleteFirstBlockAsync();
        var morning = await h.Db.AttendanceRecords.FirstAsync();
        morning.ClosedByFieldVisitId = Guid.NewGuid();
        await h.Db.SaveChangesAsync();

        await h.Controller.Scan(h.Scan());                       // opens the second stretch
        var second = await h.Db.AttendanceRecords.OrderBy(r => r.CheckInAtUtc).LastAsync();
        second.CheckInAtUtc = DateTime.UtcNow.AddHours(-2);
        second.CheckOutAtUtc = DateTime.UtcNow.AddHours(-1);     // and it is closed again
        second.ClosedByFieldVisitId = Guid.NewGuid();             // even by another visit
        await h.Db.SaveChangesAsync();

        var third = await h.Controller.Scan(h.Scan());

        Assert.Equal("AlreadyCompleted", Error(third));
        Assert.Equal(2, (await h.BlocksAsync()).Count);
    }

    private sealed class StubFace : IFaceMatchService
    {
        public bool Enabled => false;
        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));
        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default) => Task.FromResult(-1);
    }

    private sealed class StubQuery : IAttendanceQueryService
    {
        public Task<IReadOnlyList<AttendanceRecordDto>> GetOwnRecordsAsync(Guid e, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AttendanceRecordDto>>(new List<AttendanceRecordDto>());
        public Task<AttendanceRecordDto?> GetTodayAsync(Guid e, DateOnly date, CancellationToken ct = default)
            => Task.FromResult<AttendanceRecordDto?>(null);
        public Task<(AttendanceAccess Access, IReadOnlyList<AttendanceRecordDto> Records)> GetForEmployeeAsync(
            Guid t, Guid r, EmployeeRole role, CancellationToken ct = default)
            => Task.FromResult((AttendanceAccess.Forbidden, (IReadOnlyList<AttendanceRecordDto>)new List<AttendanceRecordDto>()));
    }
}
