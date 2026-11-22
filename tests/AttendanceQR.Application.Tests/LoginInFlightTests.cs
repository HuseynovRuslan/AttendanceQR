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
/// POST /api/auth/login against the REAL lockout store, with guesses still in flight when the next one
/// arrives — the situation a script creates and a one-request-at-a-time test never does.
///
/// The endpoint used to ask "is this key locked?", run PBKDF2, and only afterwards write the failure down.
/// Every request that arrived while earlier ones were hashing therefore saw a key with nothing recorded
/// against it, and was checked: the 8-guess budget bounded a sequential attacker and nobody else. The
/// same was true of the per-IP cap, whose counter was a read followed by a separate write.
///
/// "In flight" is made deterministic here rather than left to thread timing: while one guess is inside
/// the hasher, the hasher itself sends the next guess, so every request is still unfinished when its
/// successor starts. That is the worst case a parallel burst can produce, reproduced exactly.
/// </summary>
public class LoginInFlightTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000f7");
    private const string RightPin = "5273";
    private const string WrongPin = "0000";
    private const string Phone = "+994501234567";

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Counts every PIN actually checked, and can act while a check is "still running".</summary>
    private sealed class ReentrantHasher : IPasswordHasher
    {
        private int _verifies;
        public int Verifies => _verifies;
        public Action? DuringVerify { get; set; }

        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password)
        {
            Interlocked.Increment(ref _verifies);
            DuringVerify?.Invoke();
            return hash == "hashed:" + password;
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dbName = $"login-inflight-{Guid.NewGuid()}";
        private readonly List<AppDbContext> _contexts = new();

        public ManualClock Clock { get; } = new();
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public MemoryCacheLoginLockoutStore Lockout { get; }
        public ReentrantHasher Hasher { get; } = new();

        public Harness(int people = 1)
        {
            Lockout = new MemoryCacheLoginLockoutStore(Cache, Clock);
            using var seed = Context();
            seed.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            for (var i = 0; i < people; i++)
                seed.Employees.Add(new Employee
                {
                    TenantId = TenantA,
                    FullName = $"İşçi {i}",
                    Email = $"isci{i}@a.local",
                    PhoneNumber = PhoneNumbers.Normalize(PhoneOf(i)),
                    Role = EmployeeRole.Employee,
                    IsActive = true,
                    ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
                    PasswordHash = "hashed:" + RightPin,
                });
            seed.SaveChanges();
        }

        public static string PhoneOf(int i) => i == 0 ? Phone : $"+99450{1000000 + i}";

        private AppDbContext Context()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            return new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
        }

        /// <summary>A fresh request: its own DbContext, as each HTTP request has, sharing the singletons.</summary>
        public Task<IActionResult> Login(string identifier, string pin)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
            _contexts.Add(db);
            var controller = new AuthController(
                db, tenant, Hasher, new StubJwt(), Lockout, new StubPhotoStorage(), new StubFaceMatch(),
                new StubPush(), Cache, NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
            return controller.Login(new LoginRequest(identifier, pin));
        }

        public void Dispose()
        {
            foreach (var db in _contexts)
                db.Dispose();
            Cache.Dispose();
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

    private static (int Status, string Error, int? Remaining, int? Minutes) Answer(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        var v = obj.Value!;
        object? Prop(string name) => v.GetType().GetProperty(name)?.GetValue(v);
        return (obj.StatusCode ?? 200, Prop("error")?.ToString() ?? "", Prop("remaining") as int?, Prop("minutes") as int?);
    }

    /// <summary>
    /// Sends <paramref name="total"/> guesses, each one arriving while the previous is still being hashed.
    /// Run on the thread pool so the nested blocking waits have no synchronization context to deadlock on;
    /// the in-memory provider completes synchronously, so nothing here actually waits.
    /// </summary>
    private static Task Burst(Harness h, int total, Func<int, string> identifierFor) => Task.Run(async () =>
    {
        var sent = 1;
        h.Hasher.DuringVerify = () =>
        {
            if (sent >= total)
                return;
            var i = sent++;
            h.Login(identifierFor(i), WrongPin).GetAwaiter().GetResult();
        };
        await h.Login(identifierFor(0), WrongPin);
        h.Hasher.DuringVerify = null;
    });

    [Fact]
    public async Task Thirty_guesses_in_flight_at_once_check_no_more_than_eight_pins()
    {
        using var h = new Harness();

        await Burst(h, 30, _ => Phone);

        // Before the fix all thirty were checked: each one asked before any failure had been written.
        Assert.Equal(8, h.Hasher.Verifies);
        Assert.True(h.Lockout.IsLockedOut(LoginIdentity.LockoutKey(TenantA, Phone)));
    }

    [Fact]
    public async Task The_per_ip_cap_holds_against_guesses_in_flight_across_many_numbers()
    {
        // A spray: one PIN tried against thirty different people, so no per-person budget trips — only the
        // per-IP cap stands in the way. Five units short of it, five guesses may be checked and no more.
        using var h = new Harness(people: 30);
        h.Cache.Set("login-ip:unknown", 245, TimeSpan.FromMinutes(15));

        await Burst(h, 30, Harness.PhoneOf);

        Assert.Equal(5, h.Hasher.Verifies);
        Assert.Equal(250, h.Cache.Get<int>("login-ip:unknown"));
    }

    [Fact]
    public async Task A_success_hands_back_its_own_unit_and_pays_one_failure_back()
    {
        using var h = new Harness();
        h.Cache.Set("login-ip:unknown", 10, TimeSpan.FromMinutes(15));

        Assert.IsType<OkObjectResult>(await h.Login(Phone, RightPin));

        // Taken on the way in (11), then the unit back plus one paid back — the same 10 → 9 a success
        // has always produced, so a busy NAT full of real people still heals itself.
        Assert.Equal(9, h.Cache.Get<int>("login-ip:unknown"));
    }

    [Fact]
    public async Task Knocking_on_a_locked_account_does_not_charge_the_ip()
    {
        using var h = new Harness();
        for (var i = 0; i < 8; i++)
            await h.Login(Phone, WrongPin);
        var before = h.Cache.Get<int>("login-ip:unknown");

        var (status, _, _, _) = Answer(await h.Login(Phone, WrongPin));

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        Assert.Equal(before, h.Cache.Get<int>("login-ip:unknown"));
        Assert.Equal(8, h.Hasher.Verifies);   // and the refused guess was never checked
    }

    [Fact]
    public async Task The_answers_an_employee_reads_keep_their_shape_and_the_wait_grows()
    {
        // What LoginPage reads: "remaining" on a 401 (it warns at 3 and below) and "minutes" on a 429.
        using var h = new Harness();

        for (var left = 7; left >= 1; left--)
            Assert.Equal((401, "InvalidCredentials", (int?)left, (int?)null), Answer(await h.Login(Phone, WrongPin)));
        Assert.Equal((429, "TooManyAttempts", (int?)null, (int?)5), Answer(await h.Login(Phone, WrongPin)));
        Assert.Equal((429, "TooManyAttempts", (int?)null, (int?)5), Answer(await h.Login(Phone, WrongPin)));

        h.Clock.Now += TimeSpan.FromMinutes(5);

        // The second lockout of the day is longer, and the screen is told so rather than promised 5.
        for (var i = 0; i < 7; i++)
            await h.Login(Phone, WrongPin);
        Assert.Equal((429, "TooManyAttempts", (int?)null, (int?)15), Answer(await h.Login(Phone, WrongPin)));
    }

    [Fact]
    public async Task The_right_pin_on_the_last_attempt_still_signs_in_and_unlocks()
    {
        using var h = new Harness();
        for (var i = 0; i < 7; i++)
            await h.Login(Phone, WrongPin);

        Assert.IsType<OkObjectResult>(await h.Login(Phone, RightPin));
        Assert.False(h.Lockout.IsLockedOut(LoginIdentity.LockoutKey(TenantA, Phone)));
    }
}
