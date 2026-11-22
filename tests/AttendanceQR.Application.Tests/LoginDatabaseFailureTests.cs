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
/// A login the database could not answer does not count against the IP.
///
/// The per-IP unit is taken on the way in now, before the account lookup, so that parallel guesses cannot
/// all slip past the cap. Taken that early, a lookup that throws kept it — and behind Caddy every request
/// arrives from the proxy's own address, so that bucket is the whole platform's. A database blip at the
/// morning scan peak would fill it with 500s, and for the 15 minutes after the database came back every
/// login would be answered «Çox sayda cəhd». Nobody would have guessed anything.
/// </summary>
public class LoginDatabaseFailureTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000db01");

    /// <summary>A controller whose database is gone: the lookup throws, the way a dropped connection does.</summary>
    private static AuthController BrokenDatabase(IMemoryCache cache)
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantA);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"login-db-down-{Guid.NewGuid()}").Options,
            tenant);
        db.Dispose();
        return new AuthController(
            db, tenant, new Hasher(), new StubJwt(), new OpenLockout(), new StubPhotoStorage(), new StubFaceMatch(),
            new StubPush(), cache, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Theory]
    [InlineData("login", "login-ip:unknown")]
    [InlineData("app-login", "applogin-ip:unknown")]
    [InlineData("operator-login", "oplogin-ip:unknown")]
    public async Task A_lookup_that_throws_gives_the_ip_its_unit_back(string endpoint, string ipKey)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(ipKey, 10, TimeSpan.FromMinutes(15));
        var controller = BrokenDatabase(cache);
        var request = new LoginRequest("+994501234567", "1234");

        await Assert.ThrowsAnyAsync<Exception>(() => endpoint switch
        {
            "login" => controller.Login(request),
            "app-login" => controller.AppLogin(request),
            _ => controller.OperatorLogin(request, new AppOptions()),
        });

        // Before the fix: 11 — one unit of the platform's shared budget per 500.
        Assert.Equal(10, cache.Get<int>(ipKey));
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
        public bool Enabled => false;
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
