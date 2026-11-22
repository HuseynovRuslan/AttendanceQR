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
/// The per-IP «PIN-i unutdum» budget counts every miss, including the ones that overlap.
///
/// forgot-pin and forgot-pin/check read the counter at the top, looked the number up, and on a miss wrote
/// back the value they had read plus one. Two misses in flight together both read the same value and both
/// wrote value + 1: a burst of enumeration probes was counted as one. The overlap is reproduced exactly
/// here — a cache that, the moment the first request has read the counter, runs a whole second miss
/// before handing the (now stale) value back.
/// </summary>
public class ForgotPinThrottleCountTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000f7c1");
    private static readonly string ThrottleKey = $"forgotpin:{TenantA:N}:unknown";

    /// <summary>A real cache, except that the first read of one key runs <see cref="OnFirstRead"/> after
    /// reading and before returning — another request getting in between this one's read and its write.</summary>
    private sealed class InterleavingCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());
        public Action? OnFirstRead { get; set; }

        public bool TryGetValue(object key, out object? value)
        {
            var found = _inner.TryGetValue(key, out value);
            if (OnFirstRead is { } hook && Equals(key, ThrottleKey))
            {
                OnFirstRead = null;
                hook();
            }
            return found;
        }

        public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);
        public void Remove(object key) => _inner.Remove(key);
        public void Dispose() => _inner.Dispose();
        public int Count => _inner.TryGetValue(ThrottleKey, out int n) ? n : 0;
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dbName = $"forgot-pin-throttle-{Guid.NewGuid()}";
        private readonly List<AppDbContext> _contexts = new();
        public InterleavingCache Cache { get; } = new();

        public Harness()
        {
            using var seed = Context(out _);
            seed.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            seed.SaveChanges();
        }

        private AppDbContext Context(out TenantContext tenant)
        {
            tenant = new TenantContext();
            tenant.Resolve(TenantA);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
            _contexts.Add(db);
            return db;
        }

        /// <summary>One miss: a number nobody in the company has, by the given endpoint.</summary>
        public Task<IActionResult> Probe(string endpoint, string number)
        {
            var db = Context(out var tenant);
            var controller = new AuthController(
                db, tenant, new Hasher(), new StubJwt(), new OpenLockout(), new StubPhotoStorage(), new StubFaceMatch(),
                new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            return endpoint switch
            {
                "check" => controller.ForgotPinCheck(new ForgotPinRequest(number)),
                "file" => controller.ForgotPin(new ForgotPinRequest(number)),
                "verify" => controller.ForgotPinVerify(
                    new ForgotPinVerifyRequest(number, "device", Convert.ToBase64String([1, 2, 3])), new AppOptions()),
                _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
            };
        }

        public void Dispose()
        {
            foreach (var db in _contexts)
                db.Dispose();
            Cache.Dispose();
        }
    }

    [Theory]
    [InlineData("check")]
    [InlineData("file")]
    [InlineData("verify")]
    public async Task Two_overlapping_misses_are_two_misses(string endpoint)
    {
        using var h = new Harness();
        h.Cache.OnFirstRead = () => h.Probe(endpoint, "509990001").GetAwaiter().GetResult();

        await Task.Run(() => h.Probe(endpoint, "509990002"));

        // Before the fix check and file left 1 here: the first request wrote its stale 0 + 1 over the second's.
        Assert.Equal(2, h.Cache.Count);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("file")]
    public async Task The_cap_is_still_where_it_was(string endpoint)
    {
        using var h = new Harness();
        for (var i = 0; i < 260; i++)
            await h.Probe(endpoint, $"5099{i:D5}");

        Assert.Equal(250, h.Cache.Count);
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

    private sealed class StubPhotoStorage : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubFaceMatch : IFaceMatchService
    {
        public bool Enabled => true;
        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));
        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default) => Task.FromResult(-1);
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(
            IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
