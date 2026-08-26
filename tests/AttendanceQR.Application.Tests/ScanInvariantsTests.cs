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
/// The scan rules that ScanHandlerTests does not reach: the photo invariant, device binding, and the
/// overnight morning check-out.
///
/// The first one is the project's oldest promise and the easiest to break by accident — a check-in is
/// never blocked by anything optional. Someone whose camera fails, whose photo is corrupt, or whose
/// upload cannot be queued must still be recorded as having come to work, because that record is what
/// they are paid on. Every test here that sends a broken photo asserts the SAME thing: the day was
/// still opened.
///
/// Device binding is the other half — it is the one optional-looking check that IS allowed to refuse a
/// scan, so its boundaries need pinning too: an unknown phone inside the geofence is adopted, a
/// revoked one is not, and with auto-binding off an unknown phone is refused rather than silently
/// bound.
/// </summary>
public class ScanInvariantsTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d4");
    private const double OfficeLat = 40.4093;
    private const double OfficeLng = 49.8671;

    /// <summary>A 1x1 PNG — small, valid, and decodable, so only the code under test decides its fate.</summary>
    private const string TinyPng =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AttendanceController Controller { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public Location Location { get; }
        private readonly IQrTokenService _qr;

        public Harness(bool autoBind = true, TimeOnly? shiftStart = null, TimeOnly? shiftEnd = null)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"scan-inv-{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "Test", Slug = "test", DisplayName = "Test", IsActive = true });
            Location = new Location
            {
                Id = LocationId,
                TenantId = TenantId,
                Name = "Bas ofis",
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
                FullName = "Test Isci",
                Email = "test@baki.local",
                LocationId = LocationId,
                Role = EmployeeRole.Employee,
                IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow,
                PasswordHash = "x",
            });
            Db.SaveChanges();

            _qr = new QrTokenService(Options.Create(new QrTokenOptions
            {
                Secret = "test-secret-key-for-scan-invariant-tests",
                TtlSeconds = 300,
            }));

            Controller = new AttendanceController(
                Db, _qr, new StubQuery(), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(), new StubFace(),
                new DeviceBindingOptions { AutoBind = autoBind },
                new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AttendanceController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContextFor(EmployeeId) },
            };
        }

        public string ValidToken(int version = 1) => _qr.Generate(LocationId, version);

        public ScanRequest Scan(string? photo = null, string device = "device-fp-1") =>
            new(ValidToken(), device, OfficeLat, OfficeLng,
                PhotoBase64: photo, ClientScanId: null, ClientTimestampUtc: null, Offline: false);

        /// <summary>Today's row for the harness employee, read fresh — the assertions are about what was
        /// actually written, not about what the response said.</summary>
        public AttendanceRecord? TodayRecord()
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku")));
            return Db.AttendanceRecords.AsNoTracking()
                .FirstOrDefault(r => r.EmployeeId == EmployeeId && r.AttendanceDate == today);
        }

        public void BindDevice(string fingerprint, bool active) => Db.DeviceBindings.Add(new DeviceBinding
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = EmployeeId,
            DeviceFingerprint = fingerprint,
            IsActive = active,
            BoundVia = DeviceBindingOrigin.AutoBind,
            BoundAtUtc = DateTime.UtcNow.AddDays(-10),
            LastSeenAtUtc = DateTime.UtcNow.AddDays(-1),
            RevokedAtUtc = active ? null : DateTime.UtcNow.AddDays(-2),
        });

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

    private static string ActionOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var prop = ok.Value!.GetType().GetProperty("action");
        return prop?.GetValue(ok.Value)?.ToString() ?? "";
    }

    private static string ErrorOf(IActionResult result)
    {
        var value = result switch
        {
            ObjectResult o => o.Value,
            _ => null,
        };
        return value?.GetType().GetProperty("error")?.GetValue(value)?.ToString() ?? "";
    }

    // --- the photo invariant: optional things flag, they never block --------------

    [Fact]
    public async Task A_corrupt_photo_does_not_stop_the_check_in()
    {
        using var h = new Harness();
        var result = await h.Controller.Scan(h.Scan(photo: "data:image/webp;base64,!!!not-base64!!!"));

        Assert.Equal("CheckIn", ActionOf(result));
        Assert.NotNull(h.TodayRecord()?.CheckInAtUtc);
        // Nothing was queued for upload — but the day is open, which is the part their pay depends on.
        Assert.Empty(h.Db.PendingPhotoUploads.AsNoTracking().ToList());
    }

    [Fact]
    public async Task An_empty_photo_string_does_not_stop_the_check_in()
    {
        using var h = new Harness();
        Assert.Equal("CheckIn", ActionOf(await h.Controller.Scan(h.Scan(photo: "   "))));
        Assert.NotNull(h.TodayRecord()?.CheckInAtUtc);
    }

    [Fact]
    public async Task An_absurdly_large_photo_is_dropped_and_the_check_in_still_stands()
    {
        using var h = new Harness();
        // 3 MB decoded — past the 2 MB sanity bound the handler refuses to queue.
        var huge = "data:image/webp;base64," + Convert.ToBase64String(new byte[3 * 1024 * 1024]);

        Assert.Equal("CheckIn", ActionOf(await h.Controller.Scan(h.Scan(photo: huge))));
        Assert.NotNull(h.TodayRecord()?.CheckInAtUtc);
        Assert.Empty(h.Db.PendingPhotoUploads.AsNoTracking().ToList());
    }

    [Fact]
    public async Task A_good_photo_is_queued_for_upload()
    {
        using var h = new Harness();
        Assert.Equal("CheckIn", ActionOf(await h.Controller.Scan(h.Scan(photo: TinyPng))));

        var queued = Assert.Single(h.Db.PendingPhotoUploads.AsNoTracking().ToList());
        Assert.Equal(h.EmployeeId, queued.EmployeeId);
        Assert.Equal(h.TodayRecord()!.Id, queued.RecordId);
        Assert.NotEmpty(queued.Bytes);
    }

    // --- device binding: the one optional-looking check that may refuse -----------

    [Fact]
    public async Task An_unknown_phone_inside_the_geofence_is_adopted()
    {
        using var h = new Harness(autoBind: true);
        Assert.Equal("CheckIn", ActionOf(await h.Controller.Scan(h.Scan(device: "brand-new-phone"))));

        var binding = Assert.Single(h.Db.DeviceBindings.AsNoTracking().Where(d => d.EmployeeId == h.EmployeeId).ToList());
        Assert.Equal("brand-new-phone", binding.DeviceFingerprint);
        Assert.True(binding.IsActive);
    }

    [Fact]
    public async Task A_revoked_phone_is_refused_even_at_the_right_place()
    {
        using var h = new Harness(autoBind: true);
        h.BindDevice("retired-phone", active: false);
        h.BindDevice("current-phone", active: true);
        h.Db.SaveChanges();

        var result = await h.Controller.Scan(h.Scan(device: "retired-phone"));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("DeviceMismatch", ErrorOf(result));
        Assert.Null(h.TodayRecord());
    }

    [Fact]
    public async Task With_auto_binding_off_an_unknown_phone_is_refused_rather_than_bound()
    {
        using var h = new Harness(autoBind: false);

        var result = await h.Controller.Scan(h.Scan(device: "unregistered-phone"));

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("NoDeviceBound", ErrorOf(result));
        Assert.Empty(h.Db.DeviceBindings.AsNoTracking().ToList());
        Assert.Null(h.TodayRecord());
    }

    [Fact]
    public async Task A_known_phone_keeps_working_without_binding_again()
    {
        using var h = new Harness(autoBind: false);
        h.BindDevice("device-fp-1", active: true);
        h.Db.SaveChanges();

        Assert.Equal("CheckIn", ActionOf(await h.Controller.Scan(h.Scan())));
        Assert.Single(h.Db.DeviceBindings.AsNoTracking().ToList());
    }

    // --- the branch that only exists because of night shifts ---------------------

    [Fact]
    public async Task A_morning_scan_closes_last_nights_shift_instead_of_opening_a_new_day()
    {
        // 22:00–06:00. The employee checked in last night; the scan under test is the one they make
        // when they go home, which lands on the NEXT calendar day and must not read as a new check-in.
        using var h = new Harness(shiftStart: new TimeOnly(22, 0), shiftEnd: new TimeOnly(6, 0));
        var baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, baku);
        if (nowLocal.Hour >= 12)
            return;   // the branch only applies before noon; outside that window there is nothing to assert

        var yesterday = DateOnly.FromDateTime(nowLocal).AddDays(-1);
        h.Db.AttendanceRecords.Add(new AttendanceRecord
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            EmployeeId = h.EmployeeId,
            LocationId = h.LocationId,
            AttendanceDate = yesterday,
            CheckInAtUtc = DateTime.UtcNow.AddHours(-8),
            Status = AttendanceStatus.OnTime,
        });
        h.Db.SaveChanges();

        Assert.Equal("CheckOut", ActionOf(await h.Controller.Scan(h.Scan())));

        var night = h.Db.AttendanceRecords.AsNoTracking().Single(r => r.AttendanceDate == yesterday);
        Assert.NotNull(night.CheckOutAtUtc);
        Assert.Null(h.TodayRecord());   // no phantom day was opened this morning
    }

    // --- a location that was switched off ----------------------------------------

    [Fact]
    public async Task A_scan_at_a_deactivated_location_is_refused()
    {
        using var h = new Harness();
        h.Location.IsActive = false;
        h.Db.SaveChanges();

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("LocationInactive", ErrorOf(result));
        Assert.Null(h.TodayRecord());
    }

    // --- stubs -------------------------------------------------------------------

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("fieldwork/k.jpg");
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
