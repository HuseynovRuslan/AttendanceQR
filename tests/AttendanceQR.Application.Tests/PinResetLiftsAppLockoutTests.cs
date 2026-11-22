using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
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
/// A new PIN has to work the moment it is handed over — in the app too.
///
/// One account is locked under four keys: its email and its phone, in the company's own login and in
/// app-login (the Android app and app.qrlog.az, which have no company subdomain and always use it). Every
/// reset path cleared only the company's two, and the manager desk and the platform console's support
/// reset cleared none. While a lock lasted 5 minutes that was an annoyance; once repeat locks grew to
/// 15 and 60 minutes, the employee an admin had just helped — locked out in the app, which is why they
/// asked — read the fresh PIN off the paper, typed it, and was told «Çox sayda cəhd» for up to an hour.
/// No sign-in, no check-in.
///
/// Every test here runs against the REAL lockout store and the REAL app-login, with the clock walked
/// through two lockouts, so a reset that forgets a key fails here and not on somebody's phone.
/// </summary>
public class PinResetLiftsAppLockoutTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000a3a3");
    private static readonly Guid OpsTenant = Guid.Parse("00000000-0000-0000-0000-00000000a3ff");
    private const string OldPin = "5273";
    private const string WrongPin = "0000";
    private const string Phone = "+994501234567";
    private const string Email = "aygun@a.local";

    public enum ResetPath { AdminCard, AdminBulk, ManagerDesk, PlatformSupport }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dbName = $"reset-app-lockout-{Guid.NewGuid()}";
        private readonly List<AppDbContext> _contexts = new();

        public ManualClock Clock { get; } = new();
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public MemoryCacheLoginLockoutStore Lockout { get; }
        public AppOptions Options { get; }

        public Guid Branch { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid OperatorId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();

        public Harness()
        {
            Lockout = new MemoryCacheLoginLockoutStore(Cache, Clock);
            Options = new AppOptions { SuperAdminEmployeeIds = OperatorId.ToString() };

            using var seed = Context(TenantA);
            seed.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            seed.Tenants.Add(new Tenant { Id = OpsTenant, Name = "Ops", Slug = "ops", DisplayName = "Ops", IsActive = true });
            seed.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantA, Name = "Filial", IsActive = true, WorkDaysMask = 127, QrVersion = 1,
            });
            seed.Employees.Add(Person(AdminId, TenantA, "Şirkət Admini", EmployeeRole.Admin, "admin@a.local", null));
            seed.Employees.Add(Person(ManagerId, TenantA, "Filial Meneceri", EmployeeRole.Manager, "menecer@a.local", null));
            seed.Employees.Add(Person(StaffId, TenantA, "Aygün", EmployeeRole.Employee, Email, PhoneNumbers.Normalize(Phone)));
            // The operator works from another company entirely: the console's ambient tenant is NOT the
            // customer's, so a reset there that keyed on it would clear the wrong company's lock.
            seed.Employees.Add(Person(OperatorId, OpsTenant, "Platforma Operatoru", EmployeeRole.Admin, "op@ops.local", null));
            seed.ManagedLocations.Add(new ManagedLocation { TenantId = TenantA, EmployeeId = ManagerId, LocationId = Branch });
            seed.SaveChanges();
        }

        private Employee Person(Guid id, Guid tenant, string name, EmployeeRole role, string email, string? phone) => new()
        {
            Id = id, TenantId = tenant, FullName = name, Role = role, LocationId = Branch, IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow.AddDays(-30), PasswordHash = "hashed:" + OldPin,
            Email = email, PhoneNumber = phone,
        };

        /// <summary>A fresh request's DbContext. Null = the app shell, where nothing resolved a tenant.</summary>
        private AppDbContext Context(Guid? tenantId) => Context(tenantId, out _);

        private AppDbContext Context(Guid? tenantId, out TenantContext tenant)
        {
            tenant = new TenantContext();
            if (tenantId.HasValue)
                tenant.Resolve(tenantId.Value);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
            _contexts.Add(db);
            return db;
        }

        private AuthController Auth(Guid? tenantId)
        {
            var db = Context(tenantId, out var tenant);
            return new AuthController(
                db, tenant, new Hasher(), new StubJwt(), Lockout, new StubPhotoStorage(),
                new StubFaceMatch(), new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        public Task<IActionResult> AppLogin(string identifier, string pin) =>
            Auth(null).AppLogin(new LoginRequest(identifier, pin));

        public Task<IActionResult> SubdomainLogin(string identifier, string pin) =>
            Auth(TenantA).Login(new LoginRequest(identifier, pin));

        private static ControllerContext As(Guid who, EmployeeRole role) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", who.ToString()), new Claim("role", role.ToString())], "test")),
            },
        };

        /// <summary>Hands the staff member a new PIN by the given path and returns it, as the screen shows it.</summary>
        public async Task<string> Reset(ResetPath path)
        {
            IActionResult result = path switch
            {
                ResetPath.AdminCard => await Admin().ResetPin(StaffId),
                ResetPath.AdminBulk => await Admin().BulkResetPin(new BulkResetPinRequest([StaffId])),
                ResetPath.ManagerDesk => await new ManagerController(
                        Context(TenantA), new Hasher(), new StubSummary(), Options, lockout: Lockout)
                    { ControllerContext = As(ManagerId, EmployeeRole.Manager) }.ResetPin(StaffId),
                ResetPath.PlatformSupport => await Super().ResetUserPin(StaffId),
                _ => throw new ArgumentOutOfRangeException(nameof(path)),
            };

            var ok = Assert.IsType<OkObjectResult>(result);
            var body = ok.Value!;
            if (path == ResetPath.AdminBulk)
                body = Assert.Single((IEnumerable<object>)Prop(body, "issued")!);
            return Assert.IsType<string>(Prop(body, "tempPin"));
        }

        private AdminController Admin() =>
            new(Context(TenantA), Microsoft.Extensions.Options.Options.Create(new InvitationOptions()),
                new Hasher(), Lockout, Options, new StubPhotoStorage(), NullLogger<AdminController>.Instance)
            {
                ControllerContext = As(AdminId, EmployeeRole.Admin),
            };

        private SuperAdminController Super()
        {
            var db = Context(OpsTenant, out var tenant);
            return new SuperAdminController(db, tenant, new Hasher(), new StubJwt(), Options, lockout: Lockout)
            {
                ControllerContext = As(OperatorId, EmployeeRole.Admin),
            };
        }

        public void Dispose()
        {
            foreach (var db in _contexts)
                db.Dispose();
            Cache.Dispose();
        }
    }

    private static object? Prop(object v, string name) => v.GetType().GetProperty(name)?.GetValue(v);

    private static (int Status, string Error, int? Minutes) Answer(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        return (obj.StatusCode ?? 200, Prop(obj.Value!, "error")?.ToString() ?? "", Prop(obj.Value!, "minutes") as int?);
    }

    /// <summary>
    /// The employee's morning: eight wrong PINs in the app, the 5-minute wait, eight more — now a 15-minute
    /// lock — and a walk to whoever can reset it. On the subdomain too, for good measure, under the email.
    /// </summary>
    private static async Task LockThemselvesOut(Harness h)
    {
        for (var i = 0; i < 8; i++)
            await h.AppLogin(Phone, WrongPin);
        h.Clock.Now += TimeSpan.FromMinutes(5);
        for (var i = 0; i < 8; i++)
            await h.AppLogin(Phone, WrongPin);
        for (var i = 0; i < 8; i++)
            await h.SubdomainLogin(Email, WrongPin);

        // The precondition the rest stands on: even the right PIN is refused, and the second lock is the long one.
        Assert.Equal((429, "TooManyAttempts", (int?)15), Answer(await h.AppLogin(Phone, OldPin)));
        Assert.Equal(429, Answer(await h.SubdomainLogin(Email, OldPin)).Status);

        h.Clock.Now += TimeSpan.FromMinutes(1);
    }

    // The «PIN-i unutdum» queue is not in the theories: AdminPinResetController.Resolve claims its row with
    // ExecuteUpdateAsync, which the in-memory provider cannot run. It is held to the same clearing by the
    // source guard below instead.
    [Theory]
    [InlineData(ResetPath.AdminCard)]
    [InlineData(ResetPath.AdminBulk)]
    [InlineData(ResetPath.ManagerDesk)]
    [InlineData(ResetPath.PlatformSupport)]
    public async Task The_new_pin_signs_in_through_the_app_straight_away(ResetPath path)
    {
        using var h = new Harness();
        await LockThemselvesOut(h);

        var tempPin = await h.Reset(path);

        // Before the fix: 429 { minutes: 14 } — and after a third lock, about an hour.
        Assert.IsType<OkObjectResult>(await h.AppLogin(Phone, tempPin));
    }

    [Theory]
    [InlineData(ResetPath.AdminCard)]
    [InlineData(ResetPath.ManagerDesk)]
    [InlineData(ResetPath.PlatformSupport)]
    public async Task And_through_the_company_login_and_the_apps_email_key(ResetPath path)
    {
        using var h = new Harness();
        await LockThemselvesOut(h);
        // The app also knows them by email — a lock earned there is lifted with the rest.
        for (var i = 0; i < 8; i++)
            await h.AppLogin(Email, WrongPin);

        var tempPin = await h.Reset(path);

        Assert.IsType<OkObjectResult>(await h.SubdomainLogin(Email, tempPin));
        Assert.IsType<OkObjectResult>(await h.AppLogin(Email, tempPin));
    }

    [Fact]
    public void The_pin_queue_clears_through_the_same_helper()
    {
        var body = File.ReadAllText(Path.Combine(SourceDir(), "src", "AttendanceQR.Api", "Controllers",
            "AdminPinResetController.cs"));
        var resolve = body[body.IndexOf("public async Task<IActionResult> Resolve(", StringComparison.Ordinal)..];
        resolve = resolve[..resolve.IndexOf("public async Task<IActionResult> Dismiss(", StringComparison.Ordinal)];

        Assert.Contains("LoginLockouts.ClearFor(", resolve);
    }

    [Fact]
    public void Nobody_spells_the_app_login_key_by_hand()
    {
        // A clearer that builds "applogin:…" itself clears nothing the day app-login's spelling changes, and
        // says nothing about it. LoginIdentity.AppLoginKey is the one place the key is made.
        var api = Path.Combine(SourceDir(), "src", "AttendanceQR.Api");
        var offenders = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) != "LoginIdentity.cs")
            .Where(f => File.ReadAllText(f).Contains("\"applogin:", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string SourceDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void ClearFor_lifts_all_four_keys_and_leaves_the_blank_identifier_alone()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheLoginLockoutStore(cache, clock);
        var person = new Employee { TenantId = TenantA, Email = Email, PhoneNumber = PhoneNumbers.Normalize(Phone) };
        // A different spelling of the same number than the one stored: the keys must still meet.
        var keys = new[]
        {
            LoginIdentity.LockoutKey(TenantA, Email),
            LoginIdentity.LockoutKey(TenantA, "050 123 45 67"),
            LoginIdentity.AppLoginKey(Email.ToUpperInvariant()),
            LoginIdentity.AppLoginKey("(050) 123-45-67"),
        };
        var blank = LoginIdentity.AppLoginKey("");
        foreach (var key in keys.Append(blank))
            for (var i = 0; i < 8; i++)
                store.BeginAttempt(key);

        LoginLockouts.ClearFor(store, person);

        Assert.All(keys, key => Assert.False(store.IsLockedOut(key), key));
        // Every blank login form spends from this one; a reset of one person is no reason to lift it.
        Assert.True(store.IsLockedOut(blank));
    }

    [Fact]
    public void ClearFor_takes_the_accounts_own_company_not_the_callers()
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheLoginLockoutStore(cache, clock);
        var person = new Employee { TenantId = TenantA, Email = Email };
        var otherCompanysNamesake = LoginIdentity.LockoutKey(OpsTenant, Email);
        for (var i = 0; i < 8; i++)
        {
            store.BeginAttempt(LoginIdentity.LockoutKey(TenantA, Email));
            store.BeginAttempt(otherCompanysNamesake);
        }

        LoginLockouts.ClearFor(store, person);

        Assert.False(store.IsLockedOut(LoginIdentity.LockoutKey(TenantA, Email)));
        Assert.True(store.IsLockedOut(otherCompanysNamesake));
    }

    private sealed class Hasher : IPasswordHasher
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

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url = null, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
