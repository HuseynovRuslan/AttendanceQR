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
/// Characterisation tests for POST /api/auth/login — the door every employee walks through twice a
/// day, and the one place where being helpful is a security bug.
///
/// The parts worth pinning are the ones that are invisible from the outside: a wrong PIN, an unknown
/// number, a deactivated account and a not-yet-activated one all have to answer THE SAME WAY, because
/// any difference tells a stranger which phone numbers belong to real staff. The rest is the plumbing
/// people actually notice — logging in by phone number as well as by email (staff have phones, not
/// email), the per-identifier lockout, and the per-IP cap that exists because a whole site shares one
/// router.
///
/// Not covered here on purpose: PIN strength (PinRulesTests), the lockout store's own arithmetic
/// (LoginLockoutStoreTests), the tenant-less native-app path (ForgotPinAppShellTests covers the
/// cross-tenant resolution it shares), and temporary-PIN gating (TemporaryPinGateTests).
/// </summary>
public class LoginEndpointTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000e5");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000e6");

    private const string RightPin = "5273";
    private const string WrongPin = "0000";

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AuthController Controller { get; }
        public ConfigurableLockout Lockout { get; } = new();
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());

        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid InactiveId { get; } = Guid.NewGuid();
        public Guid NotActivatedId { get; } = Guid.NewGuid();
        public Guid OtherTenantId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"login-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });

            // The everyday account: a phone number, an email, an activated and active row.
            Db.Employees.Add(Person(EmployeeId, TenantA, "Adi Isci", "isci@a.local", "+994501234567", active: true, activated: true));
            // Deactivated — the CleanFix lockout taught us this must fail like a wrong PIN, not differently.
            Db.Employees.Add(Person(InactiveId, TenantA, "Deaktiv", "deaktiv@a.local", "+994501111111", active: false, activated: true));
            // Invited but never activated: the temp-PIN flow owns this account until they set a PIN.
            Db.Employees.Add(Person(NotActivatedId, TenantA, "Aktivlesmemis", "yeni@a.local", "+994502222222", active: true, activated: false));
            // Another company's employee, reachable only if the tenant filter ever slips.
            Db.Employees.Add(Person(OtherTenantId, TenantB, "Basqa Sirket", "isci@b.local", "+994503333333", active: true, activated: true));
            Db.SaveChanges();

            Controller = new AuthController(
                Db, tenant, new StubHasher(), new StubJwt(), Lockout, new StubPhotoStorage(),
                new StubFaceMatch(), new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        private static Employee Person(
            Guid id, Guid tenantId, string name, string email, string phone, bool active, bool activated) => new()
        {
            Id = id,
            TenantId = tenantId,
            FullName = name,
            Email = email,
            PhoneNumber = PhoneNumbers.Normalize(phone),
            Role = EmployeeRole.Employee,
            IsActive = active,
            ActivatedAtUtc = activated ? DateTime.UtcNow.AddDays(-30) : null,
            PasswordHash = "hashed:" + RightPin,
        };

        public Task<IActionResult> Login(string identifier, string pin) =>
            Controller.Login(new LoginRequest(identifier, pin));

        public void Dispose()
        {
            Db.Dispose();
            Cache.Dispose();
        }
    }

    /// <summary>A lockout store a test can put into any state, unlike the real one's internal timers.</summary>
    private sealed class ConfigurableLockout : ILoginLockoutStore
    {
        public bool Locked { get; set; }
        public int RemainingOnFailure { get; set; } = 4;
        public List<string> Failures { get; } = new();
        public List<string> Successes { get; } = new();

        public int LockoutMinutes => 15;
        public bool IsLockedOut(string key) => Locked;
        public int RecordFailure(string key)
        {
            Failures.Add(key);
            return RemainingOnFailure;
        }
        public void RecordSuccess(string key) => Successes.Add(key);
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubJwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token-for:" + employee.Id;
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes, bool readOnly = false) => "imp";
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

    private static string TokenOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ok.Value!.GetType().GetProperty("token")?.GetValue(ok.Value)?.ToString() ?? "";
    }

    private static (int Status, string Error) FailureOf(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var error = obj.Value?.GetType().GetProperty("error")?.GetValue(obj.Value)?.ToString() ?? "";
        return (obj.StatusCode ?? 0, error);
    }

    // --- the way in --------------------------------------------------------------

    [Fact]
    public async Task Signing_in_with_a_phone_number_works()
    {
        using var h = new Harness();
        Assert.Equal("token-for:" + h.EmployeeId, TokenOf(await h.Login("+994501234567", RightPin)));
    }

    [Theory]
    [InlineData("0501234567")]
    [InlineData("501234567")]
    [InlineData(" +994 50 123 45 67 ")]
    public async Task The_phone_number_is_accepted_however_it_is_typed(string typed)
    {
        // Staff type their number the way they say it out loud. Normalisation is what makes that work,
        // and it is the reason the login screen can ask for a phone number at all.
        using var h = new Harness();
        Assert.Equal("token-for:" + h.EmployeeId, TokenOf(await h.Login(typed, RightPin)));
    }

    [Fact]
    public async Task Signing_in_with_an_email_still_works()
    {
        // The screen says "Telefon nömrəsi" now, but admins created before that keep their email.
        using var h = new Harness();
        Assert.Equal("token-for:" + h.EmployeeId, TokenOf(await h.Login("isci@a.local", RightPin)));
    }

    [Fact]
    public async Task A_successful_login_clears_the_lockout_budget()
    {
        using var h = new Harness();
        await h.Login("+994501234567", RightPin);
        Assert.Single(h.Lockout.Successes);
        Assert.Empty(h.Lockout.Failures);
    }

    // --- every refusal looks the same --------------------------------------------

    [Fact]
    public async Task A_wrong_pin_is_refused_and_counted()
    {
        using var h = new Harness();
        var (status, error) = FailureOf(await h.Login("+994501234567", WrongPin));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal("InvalidCredentials", error);
        Assert.Single(h.Lockout.Failures);
    }

    [Fact]
    public async Task An_unknown_number_is_refused_exactly_like_a_wrong_pin()
    {
        using var h = new Harness();
        var known = FailureOf(await h.Login("+994501234567", WrongPin));
        var unknown = FailureOf(await h.Login("+994509999999", WrongPin));

        // Identical status AND identical body: the difference between "wrong PIN" and "no such person"
        // is exactly what an attacker enumerating numbers is looking for.
        Assert.Equal(known, unknown);
    }

    [Fact]
    public async Task A_deactivated_account_is_refused_the_same_way()
    {
        using var h = new Harness();
        var (status, error) = FailureOf(await h.Login("+994501111111", RightPin));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal("InvalidCredentials", error);
    }

    [Fact]
    public async Task An_account_that_never_activated_cannot_sign_in_yet()
    {
        using var h = new Harness();
        var (status, error) = FailureOf(await h.Login("+994502222222", RightPin));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal("InvalidCredentials", error);
    }

    [Fact]
    public async Task Another_companys_employee_does_not_exist_here()
    {
        // The whole tenancy model in one assertion: correct credentials, wrong company, no way in.
        using var h = new Harness();
        var (status, error) = FailureOf(await h.Login("isci@b.local", RightPin));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal("InvalidCredentials", error);
    }

    // --- the two brakes ----------------------------------------------------------

    [Fact]
    public async Task A_locked_out_identifier_is_told_to_wait_without_the_pin_being_checked()
    {
        using var h = new Harness();
        h.Lockout.Locked = true;

        var (status, error) = FailureOf(await h.Login("+994501234567", RightPin));

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Equal("TooManyAttempts", error);
        Assert.Empty(h.Lockout.Failures);   // a locked door is not a failed attempt
    }

    [Fact]
    public async Task The_last_attempt_before_a_lock_answers_429_rather_than_401()
    {
        using var h = new Harness();
        h.Lockout.RemainingOnFailure = 0;

        var (status, error) = FailureOf(await h.Login("+994501234567", WrongPin));

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Equal("TooManyAttempts", error);
    }

    [Fact]
    public async Task An_ip_that_has_burned_through_its_budget_is_stopped_before_any_work()
    {
        using var h = new Harness();
        h.Cache.Set("login-ip:unknown", 250, TimeSpan.FromMinutes(15));

        var (status, error) = FailureOf(await h.Login("+994501234567", RightPin));

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Equal("TooManyAttempts", error);
        // Not even the lockout store was consulted — that is the point of checking the IP first.
        Assert.Empty(h.Lockout.Failures);
        Assert.Empty(h.Lockout.Successes);
    }

    [Fact]
    public async Task An_empty_identifier_is_refused_without_a_lookup()
    {
        using var h = new Harness();
        var (status, error) = FailureOf(await h.Login("", RightPin));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Equal("InvalidCredentials", error);
    }
}
