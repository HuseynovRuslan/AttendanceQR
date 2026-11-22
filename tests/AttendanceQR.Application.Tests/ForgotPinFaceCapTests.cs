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
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The self-service face reset's per-account cap — five faces, then the admin queue — with faces still at
/// Rekognition when the next one arrives.
///
/// The cap was read before the compare and written after it, the same shape the PIN lockout had: every
/// request that arrived while earlier ones were being compared saw the count unchanged, so somebody holding
/// the bound device (a colleague on a shared brigade phone) firing ten at once had ten faces compared
/// against a cap of five. "In flight" is made deterministic as in LoginInFlightTests: the face matcher
/// itself sends the next attempt while it is "comparing".
/// </summary>
public class ForgotPinFaceCapTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000fc01");
    private const string Phone = "501112233";
    private const string Fingerprint = "brigade-phone";
    private static readonly string Selfie = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

    private sealed class CountingFaceMatch : IFaceMatchService
    {
        private int _compares;
        public int Compares => _compares;
        public int Score { get; set; } = 60;
        public Action? DuringCompare { get; set; }

        public bool Enabled => true;
        public Task<FaceMatchOutcome> CompareAsync(byte[] reference, byte[] selfie, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _compares);
            DuringCompare?.Invoke();
            return Task.FromResult(new FaceMatchOutcome(Score, 1, FaceMatchStatus.Ok));
        }
        public Task<int> DetectFaceCountAsync(byte[] photo, CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class Storage : IPhotoStorageService
    {
        public bool Broken { get; set; }
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) =>
            Broken ? throw new IOException("bucket unreachable") : Task.FromResult(new byte[] { 1, 2, 3 });
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dbName = $"forgot-pin-face-cap-{Guid.NewGuid()}";
        private readonly List<AppDbContext> _contexts = new();

        public Guid AygunId { get; } = Guid.NewGuid();
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public CountingFaceMatch Face { get; } = new();
        public Storage Photos { get; } = new();

        public Harness()
        {
            using var seed = Context();
            seed.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            seed.Employees.Add(new Employee
            {
                Id = AygunId, TenantId = TenantA, FullName = "Aygün", PhoneNumber = Phone,
                Role = EmployeeRole.Employee, LocationId = Guid.NewGuid(), IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "hashed:1234", ReferencePhotoKey = $"ref/{AygunId}.jpg",
            });
            seed.DeviceBindings.Add(new DeviceBinding
            {
                EmployeeId = AygunId, TenantId = TenantA, DeviceFingerprint = Fingerprint, IsActive = true,
            });
            seed.SaveChanges();
        }

        private AppDbContext Context()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
            _contexts.Add(db);
            return db;
        }

        public Task<IActionResult> Verify()
        {
            var db = Context();
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            var controller = new AuthController(
                db, tenant, new Hasher(), new StubJwt(), new OpenLockout(), Photos, Face, new StubPush(), Cache,
                NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            return controller.ForgotPinVerify(new ForgotPinVerifyRequest(Phone, Fingerprint, Selfie), new AppOptions());
        }

        public string PasswordHash()
        {
            using var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, Resolved());
            return db.Employees.AsNoTracking().Single(e => e.Id == AygunId).PasswordHash;
        }

        private static TenantContext Resolved()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            return tenant;
        }

        public void Dispose()
        {
            foreach (var db in _contexts)
                db.Dispose();
            Cache.Dispose();
        }
    }

    private static bool Verified(IActionResult result) =>
        Assert.IsType<OkObjectResult>(result).Value!.ToString()!.Contains("verified = True");

    /// <summary>Sends <paramref name="total"/> attempts, each arriving while the previous one is at the matcher.</summary>
    private static Task Burst(Harness h, int total) => Task.Run(async () =>
    {
        var sent = 1;
        h.Face.DuringCompare = () =>
        {
            if (sent >= total)
                return;
            sent++;
            h.Verify().GetAwaiter().GetResult();
        };
        await h.Verify();
        h.Face.DuringCompare = null;
    });

    [Fact]
    public async Task Twelve_faces_in_flight_at_once_are_compared_no_more_than_five_times()
    {
        using var h = new Harness();

        await Burst(h, 12);

        // Before the fix all twelve reached the matcher: each read the cap before any near-miss was written.
        Assert.Equal(5, h.Face.Compares);
        Assert.Equal("hashed:1234", h.PasswordHash());
    }

    [Fact]
    public async Task One_at_a_time_the_cap_is_still_five_and_then_closed()
    {
        using var h = new Harness();
        for (var i = 0; i < 5; i++)
            Assert.False(Verified(await h.Verify()));

        // Even the right face, now: the self-service path is closed for a while; the admin queue is not.
        h.Face.Score = 99;
        Assert.False(Verified(await h.Verify()));
        Assert.Equal(5, h.Face.Compares);
    }

    [Fact]
    public async Task A_genuine_match_inside_the_cap_resets_the_pin_and_clears_the_count()
    {
        using var h = new Harness();
        for (var i = 0; i < 4; i++)
            await h.Verify();

        h.Face.Score = 99;
        Assert.True(Verified(await h.Verify()));
        Assert.NotEqual("hashed:1234", h.PasswordHash());
        Assert.False(h.Cache.TryGetValue($"pinverify:{TenantA:N}:{h.AygunId:N}", out int _));
    }

    [Fact]
    public async Task Our_own_storage_failing_does_not_spend_the_employees_attempts()
    {
        // Nothing was compared, so nothing was guessed — the cap is for faces, as it always was.
        using var h = new Harness();
        h.Photos.Broken = true;
        for (var i = 0; i < 6; i++)
            Assert.False(Verified(await h.Verify()));

        h.Photos.Broken = false;
        h.Face.Score = 99;
        Assert.True(Verified(await h.Verify()));
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubJwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token";
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, bool readOnly = false) => "imp";
    }

    private sealed class OpenLockout : ILoginLockoutStore
    {
        public LoginAttempt BeginAttempt(string key) => new(true, 7, 0);
        public bool IsLockedOut(string key) => false;
        public void RecordSuccess(string key) { }
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(
            IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
