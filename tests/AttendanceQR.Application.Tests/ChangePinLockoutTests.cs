using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
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
/// POST /api/auth/change-password asks for the CURRENT PIN, and nothing limited how often it could be asked.
/// A session that leaked — a phone left unlocked in the changing room, a token copied off a shared tablet —
/// could try all 10,000 PINs from inside the app, set a PIN of its own, and keep the account for good. The
/// login lockout already existed; it simply was not applied here. These tests run it against the real store.
/// </summary>
public class ChangePinLockoutTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c9");
    private const string RightPin = "5273";
    private const string WrongPin = "0000";
    private const string NewPin = "8471";
    private const string Phone = "+994501234567";

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public MemoryCacheLoginLockoutStore Lockout { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid OtherId { get; } = Guid.NewGuid();
        private readonly TenantContext _tenant = new();

        public Harness()
        {
            Lockout = new MemoryCacheLoginLockoutStore(Cache);
            _tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"change-pin-{Guid.NewGuid()}").Options,
                _tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Employees.Add(Person(EmployeeId, Phone));
            Db.Employees.Add(Person(OtherId, "+994507654321"));
            Db.SaveChanges();
        }

        private static Employee Person(Guid id, string phone) => new()
        {
            Id = id, TenantId = TenantId, FullName = "İşçi", Role = EmployeeRole.Employee, IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow.AddDays(-10), PasswordHash = "hashed:" + RightPin,
            PhoneNumber = PhoneNumbers.Normalize(phone),
        };

        public Task<IActionResult> Change(string current, Guid? as_ = null)
        {
            var controller = new AuthController(
                Db, _tenant, new StubHasher(), new StubJwt(), Lockout, new StubPhotoStorage(), new StubFaceMatch(),
                new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", (as_ ?? EmployeeId).ToString()),
                            new Claim("role", nameof(EmployeeRole.Employee)),
                        }, "test")),
                    },
                },
            };
            return controller.ChangePassword(new ChangePasswordRequest(current, NewPin));
        }

        public string HashOf(Guid id) => Db.Employees.AsNoTracking().Single(e => e.Id == id).PasswordHash;

        public void Dispose()
        {
            Db.Dispose();
            Cache.Dispose();
        }
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
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

    private static (int Status, string Error, int? Minutes) Answer(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var v = obj.Value!;
        return (obj.StatusCode ?? 200,
            v.GetType().GetProperty("error")?.GetValue(v)?.ToString() ?? "",
            v.GetType().GetProperty("minutes")?.GetValue(v) as int?);
    }

    [Fact]
    public async Task Eight_wrong_current_pins_lock_the_change_and_the_right_one_is_then_not_checked()
    {
        using var h = new Harness();

        for (var i = 0; i < 7; i++)
            Assert.Equal((401, "InvalidCurrentPassword", (int?)null), Answer(await h.Change(WrongPin)));
        Assert.Equal((429, "TooManyAttempts", (int?)5), Answer(await h.Change(WrongPin)));

        // The guesser has found the PIN — too late: the lock answers before the PIN is looked at, so a
        // right guess teaches them nothing and changes nothing.
        Assert.Equal((429, "TooManyAttempts", (int?)5), Answer(await h.Change(RightPin)));
        Assert.Equal("hashed:" + RightPin, h.HashOf(h.EmployeeId));
    }

    [Fact]
    public async Task The_right_current_pin_changes_it_and_gives_the_budget_back()
    {
        using var h = new Harness();
        for (var i = 0; i < 7; i++)
            await h.Change(WrongPin);

        Assert.IsType<OkObjectResult>(await h.Change(RightPin));
        Assert.Equal("hashed:" + NewPin, h.HashOf(h.EmployeeId));
        Assert.False(h.Lockout.IsLockedOut($"changepin:{h.EmployeeId:N}"));
    }

    [Fact]
    public async Task One_accounts_lock_is_its_own_and_does_not_lock_signing_in()
    {
        // Keyed on the account: one person's typos lock nobody else's change, and the separate namespace
        // means the employee who fumbles their old PIN here can still sign in with it.
        using var h = new Harness();
        for (var i = 0; i < 8; i++)
            await h.Change(WrongPin);

        Assert.IsType<OkObjectResult>(await h.Change(RightPin, h.OtherId));
        Assert.False(h.Lockout.IsLockedOut(LoginIdentity.LockoutKey(TenantId, Phone)));
    }
}
