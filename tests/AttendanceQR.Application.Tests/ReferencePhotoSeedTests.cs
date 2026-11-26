using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.Jobs;
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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The first QR-less check-in of somebody with no reference photo seeds the reference — the face every later morning is
/// compared with. The request looks at that photo first (no face, or a crowd, and it must not be promoted), but the
/// photo worker was told about the photo BEFORE that look: it loaded the record milliseconds after the scan, still
/// NotChecked, and seeded the reference from a ceiling anyway. Every later check-in was then flagged red against it.
///
/// Two halves, both here: the request holds the photo back until its verdict is written, and the worker reads the
/// verdict again right before promoting rather than trusting the copy it loaded before the upload.
/// </summary>
public class ReferencePhotoSeedTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000005ee");
    private const double Lat = 40.4093;
    private const double Lng = 49.8671;

    private sealed class Store : IDisposable
    {
        private readonly InMemoryDatabaseRoot _root = new();
        private readonly string _name = $"seed-{Guid.NewGuid()}";
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; }

        public Store(bool qrless)
        {
            Db = Context();
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Posterisiz filial", Latitude = Lat, Longitude = Lng,
                RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true, QrlessCheckIn = qrless,
            });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Yeni İşçi", LocationId = Branch, Role = EmployeeRole.Employee,
                IsActive = true, PasswordHash = "x", ActivatedAtUtc = DateTime.UtcNow.AddMonths(-1),
            });
            Db.SaveChanges();
        }

        public AppDbContext Context()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name, _root).Options, tenant);
        }

        public DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name, _root).Options;

        public void Dispose() => Db.Dispose();
    }

    // ─── the worker ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>One check-in and its queued selfie, as the request leaves them before its face check has spoken.</summary>
    private static (Guid RecordId, Guid PendingId) QueuedCheckIn(Store s)
    {
        using var db = s.Context();
        var record = new AttendanceRecord
        {
            TenantId = TenantId, EmployeeId = s.EmployeeId, LocationId = s.Branch, AttendanceDate = new DateOnly(2026, 10, 5),
            CheckInAtUtc = new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc), Status = AttendanceStatus.OnTime,
        };
        var pending = new PendingPhotoUpload
        {
            TenantId = TenantId, RecordId = record.Id, EmployeeId = s.EmployeeId, Bytes = [1, 2, 3],
            NextAttemptUtc = DateTime.UtcNow.AddSeconds(-1),
        };
        db.AttendanceRecords.Add(record);
        db.PendingPhotoUploads.Add(pending);
        db.SaveChanges();
        return (record.Id, pending.Id);
    }

    private static PhotoUploadWorker Worker(Store s, IPhotoStorageService storage)
    {
        var options = s.Options;
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp => new AppDbContext(options, sp.GetRequiredService<ITenantContext>()));
        services.AddSingleton(storage);
        return new PhotoUploadWorker(new PhotoUploadQueue(), new NoFaceQueue(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<PhotoUploadWorker>.Instance);
    }

    /// <summary>The request's face check writing its verdict while the worker's upload is still in flight.</summary>
    private static Func<Task> VerdictLands(Store s, Guid recordId, FaceMatchStatus verdict) => async () =>
    {
        await using var request = s.Context();
        var record = await request.AttendanceRecords.SingleAsync(r => r.Id == recordId);
        record.FaceMatchStatus = verdict;
        await request.SaveChangesAsync();
    };

    [Theory]
    [InlineData(FaceMatchStatus.NoFace)]
    [InlineData(FaceMatchStatus.MultiFace)]
    public async Task A_verdict_written_during_the_upload_still_keeps_an_unfit_frame_from_becoming_the_reference(FaceMatchStatus verdict)
    {
        using var s = new Store(qrless: true);
        var (recordId, pendingId) = QueuedCheckIn(s);
        var storage = new UploadingStorage();
        storage.DuringUpload = VerdictLands(s, recordId, verdict);

        await Worker(s, storage).ProcessAsync(TenantId, pendingId, CancellationToken.None);

        await using var db = s.Context();
        Assert.Equal(0, storage.ReferenceUploads);
        Assert.Null(db.Employees.AsNoTracking().Single(e => e.Id == s.EmployeeId).ReferencePhotoKey);
        var record = db.AttendanceRecords.AsNoTracking().Single(r => r.Id == recordId);
        Assert.Equal("checkin/key", record.CheckInPhotoKey);            // the check-in photo itself is stored
        Assert.Equal(verdict, record.FaceMatchStatus);                   // and the verdict is not overwritten
        Assert.Empty(db.PendingPhotoUploads.AsNoTracking());
    }

    [Fact]
    public async Task A_one_face_first_photo_still_becomes_the_reference()
    {
        using var s = new Store(qrless: true);
        var (recordId, pendingId) = QueuedCheckIn(s);
        var storage = new UploadingStorage();
        storage.DuringUpload = VerdictLands(s, recordId, FaceMatchStatus.NoReference);

        await Worker(s, storage).ProcessAsync(TenantId, pendingId, CancellationToken.None);

        await using var db = s.Context();
        Assert.Equal(1, storage.ReferenceUploads);
        Assert.Equal("reference/key", db.Employees.AsNoTracking().Single(e => e.Id == s.EmployeeId).ReferencePhotoKey);
    }

    // ─── the request ────────────────────────────────────────────────────────────────────────────────

    private static AttendanceController Scanner(Store s, PhotoUploadQueue queue, IFaceMatchService face) =>
        new(s.Db, new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-reference-seed", TtlSeconds = 300 })),
            new AttendanceQueryService(s.Db), new UploadingStorage(), new NoFaceQueue(), queue, face,
            new DeviceBindingOptions { AutoBind = true }, new AppOptions { TimeZone = "Asia/Baku" },
            new MemoryCache(new MemoryCacheOptions()), NullLogger<AttendanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", s.EmployeeId.ToString())], "test")),
                },
            },
        };

    private static readonly string Selfie = "data:image/webp;base64," + Convert.ToBase64String([1, 2, 3, 4]);

    [Fact]
    public async Task A_qrless_first_photo_reaches_the_worker_only_after_its_face_check_has_written_the_verdict()
    {
        using var s = new Store(qrless: true);
        var queue = new PhotoUploadQueue();
        var face = new CeilingFace(queue);

        var result = await Scanner(s, queue, face).Scan(new ScanRequest(null, "device-fp-1", Lat, Lng, PhotoBase64: Selfie));

        Assert.IsType<OkObjectResult>(result);
        Assert.False(face.WorkerToldBeforeVerdict, "the worker was told before the face check ran — it would seed the reference unchecked");
        Assert.True(queue.Reader.TryRead(out var hint), "the held photo was never handed to the worker");
        var pending = s.Db.PendingPhotoUploads.AsNoTracking().Single();
        Assert.Equal(pending.Id, hint.PendingId);
        Assert.True(pending.NextAttemptUtc <= DateTime.UtcNow, "released, so the worker takes it now rather than after the grace");
        Assert.Equal(FaceMatchStatus.NoFace, s.Db.AttendanceRecords.AsNoTracking().Single().FaceMatchStatus);
    }

    [Fact]
    public async Task A_poster_scan_hands_its_photo_over_at_once_as_before()
    {
        using var s = new Store(qrless: false);
        var queue = new PhotoUploadQueue();
        var face = new CeilingFace(queue);
        var token = new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-reference-seed", TtlSeconds = 300 }))
            .Generate(s.Branch, 1);

        Assert.IsType<OkObjectResult>(await Scanner(s, queue, face).Scan(new ScanRequest(token, "device-fp-1", Lat, Lng, PhotoBase64: Selfie)));

        Assert.Null(face.WorkerToldBeforeVerdict);                      // no face check in the request for a poster scan
        Assert.True(queue.Reader.TryRead(out _));
        Assert.True(s.Db.PendingPhotoUploads.AsNoTracking().Single().NextAttemptUtc <= DateTime.UtcNow);
    }

    // ─── stubs ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A selfie of the ceiling — and a note of whether the worker had already been told about it.</summary>
    private sealed class CeilingFace(PhotoUploadQueue queue) : IFaceMatchService
    {
        public bool? WorkerToldBeforeVerdict { get; private set; }
        public bool Enabled => true;

        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));

        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default)
        {
            WorkerToldBeforeVerdict = queue.Reader.TryPeek(out _);
            return Task.FromResult(0);
        }
    }

    private sealed class UploadingStorage : IPhotoStorageService
    {
        public Func<Task>? DuringUpload { get; set; }
        public int ReferenceUploads { get; private set; }

        public async Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default)
        {
            if (DuringUpload is not null)
                await DuringUpload();
            return "checkin/key";
        }

        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default)
        {
            ReferenceUploads++;
            return Task.FromResult("reference/key");
        }

        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("a");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("f");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class NoFaceQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }
}
