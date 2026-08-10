using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
/// Characterisation tests for the scan handler — the one endpoint that must never regress, because a
/// bug here means nobody can clock in at 08:00 and their pay depends on the record. It was the least
/// tested code in the app: validation, geo and the day-calculator had unit tests, but the handler
/// that orchestrates them (geofence → device → check-in/out decision, idempotency, the overnight
/// morning check-out) had none. These pin the behaviour that anti-fraud and payroll both rely on.
///
/// Runs against EF Core InMemory rather than Postgres: the scan path is branching logic over simple
/// equality/range queries, which InMemory serves faithfully, and it sidesteps the Postgres-only
/// column types elsewhere in the model that a relational test DB would trip over.
/// </summary>
public class ScanHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    // Baku, a real geofence: the office at (lat, lng) with a 150 m radius.
    private const double OfficeLat = 40.4093;
    private const double OfficeLng = 49.8671;

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AttendanceController Controller { get; }
        public StubFace Face { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Location Location { get; }
        private readonly IQrTokenService _qr;

        public Harness(TimeOnly? shiftStart = null, TimeOnly? shiftEnd = null)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"scan-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "Test", Slug = "test", DisplayName = "Test", IsActive = true });
            Location = new Location
            {
                Id = LocationId,
                TenantId = TenantId,
                Name = "Baş ofis",
                Latitude = OfficeLat,
                Longitude = OfficeLng,
                RadiusMeters = 150,
                ShiftStart = shiftStart ?? new TimeOnly(9, 0),
                ShiftEnd = shiftEnd ?? new TimeOnly(18, 0),
                LateThresholdMinutes = 15,
                QrVersion = 1,
                IsActive = true,
            };
            Db.Locations.Add(Location);
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId,
                TenantId = TenantId,
                FullName = "Test İşçi",
                Email = "test@baki.local",
                LocationId = LocationId,
                Role = EmployeeRole.Employee,
                IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow,
                PasswordHash = "x",
            });
            Db.SaveChanges();

            _qr = new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-key-for-scan-tests", TtlSeconds = 300 }));

            Face = new StubFace();
            Controller = new AttendanceController(
                Db, _qr, new StubQuery(), new StubPhoto(), new StubQueue(), Face,
                new DeviceBindingOptions { AutoBind = true },
                new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AttendanceController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContextFor(EmployeeId) },
            };
        }

        public string ValidToken(int version = 1) => _qr.Generate(LocationId, version);

        /// <summary>A scan payload at the office, for the harness's employee. Overrides let a test move
        /// the coordinate outside the fence or replay a client id.</summary>
        public ScanRequest Scan(
            string? token = null, double? lat = null, double? lng = null,
            Guid? clientScanId = null, bool offline = false, DateTime? clientTs = null) =>
            new(token ?? ValidToken(), "device-fp-1", lat ?? OfficeLat, lng ?? OfficeLng,
                PhotoBase64: null, ClientScanId: clientScanId, ClientTimestampUtc: clientTs, Offline: offline);

        private static HttpContext HttpContextFor(Guid employeeId)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", employeeId.ToString()),
                new Claim("role", nameof(EmployeeRole.Employee)),
            }, "test");
            return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }

        public void Dispose() => Db.Dispose();
    }

    // --- the checks anti-fraud depends on -----------------------------------

    [Fact]
    public async Task Valid_scan_at_the_location_checks_in()
    {
        using var h = new Harness();
        var result = await h.Controller.Scan(h.Scan());
        Assert.Equal("CheckIn", Action(result));
        Assert.Equal(1, await h.Db.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task A_scan_outside_the_radius_is_rejected()
    {
        using var h = new Harness();
        // ~1.5 km north of the fence — well past 150 m.
        var result = await h.Controller.Scan(h.Scan(lat: OfficeLat + 0.015));
        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(result));
        Assert.Equal("OutsideRadius", Error(result));
        Assert.Equal(0, await h.Db.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task An_inactive_employee_cannot_scan()
    {
        using var h = new Harness();
        var e = await h.Db.Employees.FirstAsync();
        e.IsActive = false;
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.Scan(h.Scan());
        Assert.Equal("EmployeeNotFoundOrInactive", Error(result));
        Assert.Equal(0, await h.Db.AttendanceRecords.CountAsync());
    }

    [Fact]
    public async Task A_revoked_QR_version_is_rejected_as_expired()
    {
        using var h = new Harness();
        // Admin regenerated the poster: the location moved to version 2, but this token is v1.
        h.Location.QrVersion = 2;
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.Scan(h.Scan(token: h.ValidToken(version: 1)));
        Assert.Equal("TokenExpired", Error(result));
    }

    // --- idempotency: an offline replay must not double-count ----------------

    [Fact]
    public async Task Replaying_the_same_client_scan_id_does_not_create_a_second_record()
    {
        using var h = new Harness();
        var scanId = Guid.NewGuid();

        var first = await h.Controller.Scan(h.Scan(clientScanId: scanId, offline: true, clientTs: DateTime.UtcNow));
        Assert.Equal("CheckIn", Action(first));

        // The queue re-sends the same item (its response was lost). Same client id → already recorded.
        var replay = await h.Controller.Scan(h.Scan(clientScanId: scanId, offline: true, clientTs: DateTime.UtcNow));
        Assert.Equal("AlreadyRecorded", Action(replay));
        Assert.Equal(1, await h.Db.AttendanceRecords.CountAsync());
    }

    // --- check-in → check-out lifecycle -------------------------------------

    [Fact]
    public async Task A_rapid_second_scan_is_rejected_not_checked_out()
    {
        using var h = new Harness();
        await h.Controller.Scan(h.Scan());
        // "Did it work?" — a scan seconds later must not check them straight back out.
        var second = await h.Controller.Scan(h.Scan());
        Assert.Equal("TooSoonToCheckOut", Error(second));

        var record = await h.Db.AttendanceRecords.FirstAsync();
        Assert.NotNull(record.CheckInAtUtc);
        Assert.Null(record.CheckOutAtUtc);
    }

    [Fact]
    public async Task A_scan_after_the_minimum_interval_checks_out()
    {
        using var h = new Harness();
        await h.Controller.Scan(h.Scan());

        // Push the check-in back so the second scan is a genuine check-out, not a double-tap.
        var record = await h.Db.AttendanceRecords.FirstAsync();
        record.CheckInAtUtc = DateTime.UtcNow.AddHours(-8);
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.Scan(h.Scan());
        Assert.Equal("CheckOut", Action(result));
        Assert.NotNull((await h.Db.AttendanceRecords.FirstAsync()).CheckOutAtUtc);
    }

    [Fact]
    public async Task A_third_scan_after_a_completed_day_is_rejected()
    {
        using var h = new Harness();
        await h.Controller.Scan(h.Scan());
        var record = await h.Db.AttendanceRecords.FirstAsync();
        record.CheckInAtUtc = DateTime.UtcNow.AddHours(-8);
        await h.Db.SaveChangesAsync();
        await h.Controller.Scan(h.Scan()); // check-out

        var third = await h.Controller.Scan(h.Scan());
        Assert.Equal("AlreadyCompleted", Error(third));
    }

    // --- offline clock trust window -----------------------------------------

    [Fact]
    public async Task An_offline_timestamp_far_in_the_past_is_refused_not_silently_moved_to_today()
    {
        using var h = new Harness();
        // A phone whose clock is rolled back a week is beyond the trust window (−18h). Falling back
        // to server time is what this USED to do, and it was the wrong call: it silently wrote last
        // Tuesday's scan onto today, so a real day stayed missing while a day nobody worked showed a
        // check-in. Refusing is honest — the queue drops it and tells the employee, and the admin
        // gets it on the Problems screen, which is the only path that ends in the right day being
        // fixed by hand.
        var lastWeek = DateTime.UtcNow.AddDays(-7);
        var result = await h.Controller.Scan(h.Scan(offline: true, clientTs: lastWeek));

        Assert.Equal("OfflineTooOld", Error(result));
        Assert.False(await h.Db.AttendanceRecords.AnyAsync(),
            "an out-of-window offline scan must not create a record on the wrong day");
    }

    [Fact]
    public async Task An_offline_timestamp_inside_the_trust_window_keeps_the_phones_time()
    {
        using var h = new Harness();
        // The other side of the same boundary — the case the window exists FOR. A night-shift worker
        // scans with no signal and syncs when they reach coverage hours later; that scan has to land
        // on the hour they actually arrived, not the hour their phone found a tower.
        var earlier = DateTime.UtcNow.AddHours(-6);
        var result = await h.Controller.Scan(h.Scan(offline: true, clientTs: earlier));

        Assert.Equal("CheckIn", Action(result));
        var record = await h.Db.AttendanceRecords.FirstAsync();
        Assert.True(Math.Abs((record.CheckInAtUtc!.Value - earlier).TotalMinutes) < 1,
            "an in-window offline timestamp must be kept as the arrival time");
    }

    // --- the pre-scan face hint ---------------------------------------------

    [Fact]
    public async Task The_photo_check_answers_instead_of_throwing()
    {
        // Thin, but it is the test that was missing: the IMemoryCache the hourly budget uses was added
        // to the constructor and never assigned to its field, so this endpoint threw a
        // NullReferenceException on the first line of every call. It compiled (a warning, not an
        // error), it deployed, and nothing here called this method — so the only symptom was that the
        // "your face isn't in this photo, retake it" hint quietly stopped appearing on real phones.
        using var h = new Harness();
        var result = await h.Controller.PhotoCheck(new ReferencePhotoRequest(SmallPhoto));

        Assert.Equal(-1, Convert.ToInt32(Prop(result, "faces")));
    }

    [Fact]
    public async Task The_photo_check_stops_paying_after_the_hourly_budget()
    {
        // The endpoint takes a photo from an authenticated caller and forwards it to a service that
        // charges per image, so without a cap one script is an open tap on someone else's bill.
        using var h = new Harness();
        for (var i = 0; i < 25; i++)
            await h.Controller.PhotoCheck(new ReferencePhotoRequest(SmallPhoto));

        Assert.Equal(20, h.Face.Detections);
    }

    [Fact]
    public async Task Being_out_of_budget_looks_exactly_like_not_knowing()
    {
        // -1, the same answer as "the service is off" or "the photo was unreadable". It matters that
        // the cap has no answer of its own: the client treats anything but a real count as "cannot
        // tell" and says nothing, so a capped employee loses the hint and keeps the check-in.
        using var h = new Harness();
        for (var i = 0; i < 20; i++)
            await h.Controller.PhotoCheck(new ReferencePhotoRequest(SmallPhoto));

        var capped = await h.Controller.PhotoCheck(new ReferencePhotoRequest(SmallPhoto));
        Assert.Equal(-1, Convert.ToInt32(Prop(capped, "faces")));
    }

    /// <summary>A few real bytes as base64 — enough to pass the size guard; nothing decodes it.</summary>
    private static readonly string SmallPhoto = Convert.ToBase64String(new byte[64]);

    // --- helpers to read the anonymous action results -----------------------

    private static object? Prop(IActionResult result, string name)
    {
        var value = result switch
        {
            OkObjectResult ok => ok.Value,
            ObjectResult o => o.Value,
            _ => null,
        };
        return value?.GetType().GetProperty(name)?.GetValue(value);
    }

    private static string? Action(IActionResult r) => Prop(r, "action")?.ToString();
    private static string? Error(IActionResult r) => Prop(r, "error")?.ToString();
    private static int StatusCode(IActionResult r) => r is ObjectResult o ? o.StatusCode ?? 200 : 200;

    // --- stubs: none are exercised by the scan path when no photo is sent ----

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("fieldwork/k.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private sealed class StubFace : IFaceMatchService
    {
        /// <summary>How many times the paid detection was actually reached — what the hourly budget caps.</summary>
        public int Detections { get; private set; }

        public bool Enabled => false;
        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));
        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default)
        {
            Detections++;
            return Task.FromResult(-1);
        }
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
