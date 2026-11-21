using System.Security.Claims;
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
/// «PIN-i dəyiş» (POST /api/auth/change-password) with a signed-in session.
///
/// What is pinned here:
/// - a mistyped CURRENT PIN is a 400, not a 401 — every 401 signs the device out, and the session is fine;
/// - the new PIN's shape is judged before the current PIN is looked at;
/// - wrong current PINs share a per-account budget, like a login, and a locked account is not hashed.
/// </summary>
public class ChangePasswordTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c7");
    private const string RightPin = "5273";
    private const string NewPin = "8471";

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public CountingHasher Hasher { get; } = new();
        public MemoryCacheLoginLockoutStore Lockout { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            Lockout = new MemoryCacheLoginLockoutStore(Cache);
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"change-password-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Isci", Role = EmployeeRole.Employee,
                LocationId = Guid.NewGuid(), IsActive = true, ActivatedAtUtc = DateTime.UtcNow.AddDays(-1),
                PasswordHash = "hashed:" + RightPin,
            });
            Db.SaveChanges();
        }

        public Task<IActionResult> ChangeAsync(string current, string next)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            var auth = new AuthController(
                Db, tenant, Hasher, new StubJwt(), Lockout, new StubPhotoStorage(),
                new StubFaceMatch(), new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", EmployeeId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Employee)),
                        }, "test")),
                    },
                },
            };
            return auth.ChangePassword(new ChangePasswordRequest(current, next));
        }

        public Employee Row() => Db.Employees.IgnoreQueryFilters().AsNoTracking().Single(e => e.Id == EmployeeId);

        public void Dispose()
        {
            Db.Dispose();
            Cache.Dispose();
        }
    }

    private static (int Status, string Body) Read(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        return (obj.StatusCode ?? 200, obj.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task A_wrong_current_pin_is_a_400_and_the_session_is_not_touched()
    {
        // Not 401: the client signs the device out on every 401. The token stays valid (same version).
        using var h = new Harness();

        var (status, body) = Read(await h.ChangeAsync("0000", NewPin));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("InvalidCurrentPassword", body);
        Assert.Equal(0, h.Row().TokenVersion);
        Assert.Equal("hashed:" + RightPin, h.Row().PasswordHash);
    }

    [Fact]
    public async Task A_malformed_new_pin_is_refused_before_the_current_pin_is_even_checked()
    {
        // Whatever the current PIN, a malformed new one is answered the same way, and first.
        using var h = new Harness();

        var (status, body) = Read(await h.ChangeAsync(RightPin, "12"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("PinInvalid", body);
        Assert.Equal(0, h.Hasher.Verifies);
    }

    [Fact]
    public async Task Guessing_the_current_pin_runs_out_and_a_locked_account_is_not_even_hashed()
    {
        using var h = new Harness();

        IActionResult last = null!;
        for (var i = 0; i < 8; i++)
            last = await h.ChangeAsync($"{1000 + i}", NewPin);
        // The 8th wrong guess is the one that locks (the same 8 a login allows).
        Assert.Equal(StatusCodes.Status429TooManyRequests, Read(last).Status);

        var verifiesBefore = h.Hasher.Verifies;
        // Locked: even the RIGHT PIN is refused now, without a hash run — that is what bounds the cost.
        var (status, body) = Read(await h.ChangeAsync(RightPin, NewPin));

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Contains("TooManyAttempts", body);
        Assert.Equal(verifiesBefore, h.Hasher.Verifies);
        Assert.Equal("hashed:" + RightPin, h.Row().PasswordHash);
    }

    [Fact]
    public async Task The_right_pin_still_changes_it_and_clears_the_count()
    {
        using var h = new Harness();
        await h.ChangeAsync("0000", NewPin);
        await h.ChangeAsync("0001", NewPin);

        Assert.IsType<OkObjectResult>(await h.ChangeAsync(RightPin, NewPin));

        Assert.Equal("hashed:" + NewPin, h.Row().PasswordHash);
        Assert.Equal(1, h.Row().TokenVersion);
        Assert.False(h.Lockout.IsLockedOut($"pinchange:{h.EmployeeId}"));
    }

    // --- stubs -------------------------------------------------------------------

    private sealed class CountingHasher : IPasswordHasher
    {
        public int Verifies { get; private set; }
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password)
        {
            Verifies++;
            return hash == "hashed:" + password;
        }
    }

    private sealed class StubJwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token-for:" + employee.Id;
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, bool readOnly = false) => "imp";
    }

    private sealed class StubPhotoStorage : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
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
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url = null, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
