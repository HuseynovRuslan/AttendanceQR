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
/// "PIN-i unutdum" from the single-URL native app shell (app.qrlog.az), which carries NO company
/// subdomain — so nothing resolves a tenant, and until this was fixed the fail-closed middleware
/// answered both forgot-pin endpoints with 400 TenantUnresolved. For an employee installed on that
/// shell the only PIN-recovery path they have was simply dead.
///
/// The fix mirrors app-login: find the account across every company from the identifier alone, apply
/// the same LIVE-and-activated candidate rule, and take the tenant FROM the matched row. What these
/// tests pin down:
///   • the filed PinResetRequest carries the EMPLOYEE's TenantId — a row stamped with an empty or
///     wrong tenant is invisible in /admin/pin-resets, i.e. the plea vanishes silently;
///   • an identifier two companies both use is never guessed at — verify refuses it (it hands back a
///     credential), forgot-pin files into each of them (it hands back nothing);
///   • every outcome answers with the one identical generic body, so nothing is enumerable;
///   • the per-IP throttle is charged by misses only, so the employees this exists for cannot be
///     locked out of it by each other;
///   • a self-service reset leaves the employee able to actually log in afterwards;
///   • the subdomain path still behaves exactly as before, tenant-scoped and unable to reach across.
/// </summary>
public class ForgotPinAppShellTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class Harness : IDisposable
    {
        private readonly string _dbName = $"forgot-pin-{Guid.NewGuid()}";

        public Guid AliceId { get; } = Guid.NewGuid();   // tenant A, phone unique to her
        public Guid SharedAId { get; } = Guid.NewGuid(); // tenant A, phone ALSO used in tenant B
        public Guid SharedBId { get; } = Guid.NewGuid(); // tenant B, the colliding row
        public Guid BaharId { get; } = Guid.NewGuid();   // tenant B, unrelated — proves one employee's
                                                         // traffic doesn't cost another one their turn

        public const string AlicePhone = "501112233";
        public const string SharedPhone = "555556677";
        public const string BaharPhone = "509998877";
        public const string Fingerprint = "device-abc";

        /// <summary>One cache for the whole harness, because in the app it is a singleton: the per-IP
        /// throttle only means anything if consecutive requests actually see each other's counter.</summary>
        public IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());

        public RecordingLockout Lockout { get; } = new();

        public Harness(bool sharedBActivated = true, bool sharedBActive = true)
        {
            // Seeding needs a tenant, like every other write in the app does; the controller under test
            // gets its own context below.
            using var seed = Context(TenantA);
            seed.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            seed.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            seed.Employees.Add(Person(AliceId, TenantA, "Aygün", AlicePhone));
            seed.Employees.Add(Person(SharedAId, TenantA, "Eyni A", SharedPhone));
            var sharedB = Person(SharedBId, TenantB, "Eyni B", SharedPhone);
            if (!sharedBActivated)
                sharedB.ActivatedAtUtc = null;
            sharedB.IsActive = sharedBActive;
            seed.Employees.Add(sharedB);
            seed.Employees.Add(Person(BaharId, TenantB, "Bahar", BaharPhone));
            seed.DeviceBindings.Add(new DeviceBinding
            {
                EmployeeId = AliceId, TenantId = TenantA, DeviceFingerprint = Fingerprint, IsActive = true,
            });
            seed.DeviceBindings.Add(new DeviceBinding
            {
                EmployeeId = SharedAId, TenantId = TenantA, DeviceFingerprint = Fingerprint, IsActive = true,
            });
            seed.SaveChanges();
        }

        private static Employee Person(Guid id, Guid tenantId, string name, string phone) => new()
        {
            Id = id, TenantId = tenantId, FullName = name, PhoneNumber = phone,
            Role = EmployeeRole.Employee, LocationId = Guid.NewGuid(), IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "hashed:1234",
            ReferencePhotoKey = $"ref/{id}.jpg",
        };

        private AppDbContext Context(Guid? tenantId)
        {
            var tenant = new TenantContext();
            if (tenantId.HasValue)
                tenant.Resolve(tenantId.Value);
            return new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
        }

        /// <summary>A controller as the app shell reaches it: nothing has resolved a tenant, so any
        /// filtered query would throw. Passing null is the whole point of the test.</summary>
        public AuthController AppShell(int faceScore = 99) => Build(null, faceScore);

        /// <summary>A controller as a company subdomain reaches it — the middleware resolved the tenant
        /// before the controller ran.</summary>
        public AuthController Subdomain(Guid tenantId, int faceScore = 99) => Build(tenantId, faceScore);

        private AuthController Build(Guid? tenantId, int faceScore)
        {
            var tenant = new TenantContext();
            if (tenantId.HasValue)
                tenant.Resolve(tenantId.Value);
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, tenant);
            _contexts.Add(db);

            return new AuthController(
                db, tenant, new StubHasher(), new StubJwt(), Lockout, new StubPhotoStorage(),
                new StubFaceMatch(faceScore), new StubPush(), Cache,
                NullLogger<AuthController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        private readonly List<AppDbContext> _contexts = new();

        /// <summary>Reads across every company — the assertions have to see rows wherever they landed,
        /// including the empty-tenant ones a broken stamp would produce.</summary>
        public List<PinResetRequest> AllResetRequests()
        {
            using var db = Context(TenantA);
            return db.PinResetRequests.IgnoreQueryFilters().AsNoTracking().ToList();
        }

        public List<AuditLog> AuditLogs()
        {
            using var db = Context(TenantA);
            return db.AuditLogs.IgnoreQueryFilters().AsNoTracking().ToList();
        }

        /// <summary>A context with nothing resolved — what an app-shell request actually has.</summary>
        public AppDbContext TenantLessDb()
        {
            var db = Context(null);
            _contexts.Add(db);
            return db;
        }

        public Employee Row(Guid id)
        {
            using var db = Context(TenantA);
            return db.Employees.IgnoreQueryFilters().AsNoTracking().Single(e => e.Id == id);
        }

        public void Dispose()
        {
            foreach (var db in _contexts)
                db.Dispose();
        }
    }

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubJwt : IJwtService
    {
        public string GenerateToken(Employee employee) => "token";
        public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes) => "imp";
    }

    /// <summary>Records which lockout budgets were cleared — the login screen the employee lands on
    /// next is only usable if the right ones were.</summary>
    internal sealed class RecordingLockout : ILoginLockoutStore
    {
        public List<string> Cleared { get; } = new();
        public int LockoutMinutes => 15;
        public bool IsLockedOut(string key) => false;
        public int RecordFailure(string key) => 5;
        public void RecordSuccess(string key) => Cleared.Add(key);
    }

    private sealed class StubPhotoStorage : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(new byte[] { 1, 2, 3 });
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubFaceMatch : IFaceMatchService
    {
        private readonly int _score;
        public StubFaceMatch(int score) => _score = score;
        public bool Enabled => true;
        public Task<FaceMatchOutcome> CompareAsync(byte[] reference, byte[] selfie, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(_score, 1, FaceMatchStatus.Ok));
        public Task<int> DetectFaceCountAsync(byte[] photo, CancellationToken ct = default) => Task.FromResult(1);
    }

    /// <summary>The reset alert reads PushSubscriptions, which is tenant-filtered — a stub keeps the
    /// test about the tenant resolution, not about push.</summary>
    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(
            IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
            => Task.FromResult(0);
    }

    /// <summary>Every forgot-pin outcome answers with this one body — match, miss or ambiguous.</summary>
    private static void AssertGenericOk(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("{ ok = True }", ok.Value!.ToString());
    }

    private static readonly byte[] Selfie = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
    private static string SelfieBase64 => Convert.ToBase64String(Selfie);

    // --- the premise these tests rest on -----------------------------------------------------------

    [Fact]
    public void A_tenant_less_context_really_cannot_read_a_filtered_set()
    {
        // Everything below is only meaningful if "no tenant" genuinely bites here. It does: the global
        // filter reads CurrentTenantId, which throws rather than defaulting to a company. So an app-shell
        // test that passes proves the endpoint took the cross-tenant route, not that the filter was lax.
        using var h = new Harness();
        var db = h.TenantLessDb();

        Assert.Throws<InvalidOperationException>(() => db.Employees.FirstOrDefault());
    }

    // --- forgot-pin from the app shell -------------------------------------------------------------

    [Fact]
    public async Task App_shell_files_the_request_into_the_employees_own_company()
    {
        // The bug: with no subdomain there was no tenant, so this never even reached the controller.
        using var h = new Harness();

        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.AlicePhone)));

        var filed = Assert.Single(h.AllResetRequests());
        Assert.Equal(h.AliceId, filed.EmployeeId);
        // The point of the whole exercise: stamped with the employee's company, so it shows up in THAT
        // company's /admin/pin-resets. An empty TenantId here means the request silently disappeared.
        Assert.Equal(TenantA, filed.TenantId);
        Assert.NotEqual(Guid.Empty, filed.TenantId);
        Assert.Equal(PinResetStatus.Pending, filed.Status);

        var audit = Assert.Single(h.AuditLogs());
        Assert.Equal(AuditEventType.PinResetRequested, audit.EventType);
        Assert.Equal(TenantA, audit.TenantId);
    }

    [Fact]
    public async Task App_shell_files_into_every_company_that_uses_a_shared_identifier()
    {
        // Refusing an ambiguous number would make this a permanent, silent dead end: the screen says
        // "Sorğu göndərildi" and no admin ever sees anything. Nothing is guessed at — both pleas are
        // filed, and each admin recognises their own employee. Only the credential-issuing path
        // (verify) has to refuse.
        using var h = new Harness();

        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.SharedPhone)));

        var filed = h.AllResetRequests();
        Assert.Equal(2, filed.Count);
        Assert.Equal(
            new[] { (h.SharedAId, TenantA), (h.SharedBId, TenantB) }.OrderBy(x => x.Item1).ToList(),
            filed.Select(r => (r.EmployeeId, r.TenantId)).OrderBy(x => x.Item1).ToList());
        // Each row lands in ITS OWN company's queue — a plea filed under the wrong tenant is worse
        // than none, because the admin who could act on it never sees it.
        Assert.All(filed, r => Assert.NotEqual(Guid.Empty, r.TenantId));
    }

    [Fact]
    public async Task A_deactivated_namesake_elsewhere_does_not_shadow_a_live_employee()
    {
        // Someone who left one tenant and joined another keeps their phone number. The leaver's dead
        // row must not cost the live employee the self-service path — app-login already ignores it, and
        // this has to match, or the ordinary case (three companies, one labour market) is a dead end.
        using var h = new Harness(sharedBActive: false);

        var result = Assert.IsType<OkObjectResult>(await h.AppShell().ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.SharedPhone, Harness.Fingerprint, SelfieBase64)));
        Assert.Contains("verified = True", result.Value!.ToString());

        Assert.NotEqual("hashed:1234", h.Row(h.SharedAId).PasswordHash);
        Assert.Equal("hashed:1234", h.Row(h.SharedBId).PasswordHash); // the dead row is untouched
    }

    [Fact]
    public async Task A_never_activated_namesake_elsewhere_does_not_make_the_number_ambiguous()
    {
        // Only an activated account has a PIN to forget. A leftover invite row carrying the same number
        // in another company must not cost a real employee the one path they have left.
        using var h = new Harness(sharedBActivated: false);

        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.SharedPhone)));

        var filed = Assert.Single(h.AllResetRequests());
        Assert.Equal(h.SharedAId, filed.EmployeeId);
        Assert.Equal(TenantA, filed.TenantId);
    }

    [Fact]
    public async Task App_shell_answers_an_unknown_identifier_identically_and_files_nothing()
    {
        using var h = new Harness();

        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest("994705559999")));

        Assert.Empty(h.AllResetRequests());
        Assert.Empty(h.AuditLogs());
    }

    [Fact]
    public async Task App_shell_still_files_only_one_open_request_per_employee()
    {
        // The duplicate guard queries a tenant-FILTERED set, so it only works if the account's tenant
        // really was resolved into the request — otherwise the second tap would throw or double-file.
        using var h = new Harness();

        await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.AlicePhone));
        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.AlicePhone)));

        Assert.Single(h.AllResetRequests());
    }

    // --- the per-IP throttle: it must bound probes, not employees ----------------------------------

    // Comfortably past the controller's cap. Behind Caddy the API sees the proxy's address on every
    // request (nothing trusts X-Forwarded-For), so in production this bucket is shared by everybody —
    // which is exactly why what does and does not spend from it matters.
    private const int PastTheCap = 300;

    [Fact]
    public async Task Employees_who_are_recognised_never_exhaust_the_shared_bucket()
    {
        // The failure this guards against is the one that made today's incident: a wave of locked-out
        // employees on one shared address, and the (N+1)th of them silently gets nothing — the very
        // "Sizi tanıya bilmədik" screen this change exists to remove.
        using var h = new Harness();
        var api = h.AppShell();

        for (var i = 0; i < PastTheCap; i++)
            await api.ForgotPin(new ForgotPinRequest(Harness.AlicePhone));

        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.BaharPhone)));

        var filed = h.AllResetRequests();
        Assert.Equal(2, filed.Count); // Alice once (deduped), and Bahar still got her turn
        Assert.Contains(filed, r => r.EmployeeId == h.BaharId && r.TenantId == TenantB);
    }

    [Fact]
    public async Task A_run_of_unknown_identifiers_still_spends_the_bucket()
    {
        // The other half of the same rule: the cap has to keep bounding enumeration and admin-queue
        // flooding, and every probe is a miss, so misses are what it charges.
        using var h = new Harness();
        var api = h.AppShell();

        for (var i = 0; i < PastTheCap; i++)
            await api.ForgotPin(new ForgotPinRequest($"99470555{i:D4}"));

        AssertGenericOk(await h.AppShell().ForgotPin(new ForgotPinRequest(Harness.AlicePhone)));

        Assert.Empty(h.AllResetRequests());
    }

    // --- the subdomain path must not have moved ----------------------------------------------------

    [Fact]
    public async Task A_company_subdomain_files_the_request_exactly_as_before()
    {
        using var h = new Harness();

        AssertGenericOk(await h.Subdomain(TenantA).ForgotPin(new ForgotPinRequest(Harness.AlicePhone)));

        var filed = Assert.Single(h.AllResetRequests());
        Assert.Equal(h.AliceId, filed.EmployeeId);
        Assert.Equal(TenantA, filed.TenantId);
    }

    [Fact]
    public async Task A_company_subdomain_cannot_reach_an_employee_of_another_company()
    {
        // Tenant B's subdomain naming an account that only exists in tenant A gets the generic 200 and
        // files nothing — the cross-tenant lookup must NOT have leaked into the scoped path.
        using var h = new Harness();

        AssertGenericOk(await h.Subdomain(TenantB).ForgotPin(new ForgotPinRequest(Harness.AlicePhone)));

        Assert.Empty(h.AllResetRequests());
    }

    [Fact]
    public async Task A_company_subdomain_resolves_its_own_side_of_a_shared_number()
    {
        // Ambiguity is an app-shell problem only: on a subdomain the query filter already answers the
        // "which company" question, so a number used in both companies still works there.
        using var h = new Harness();

        AssertGenericOk(await h.Subdomain(TenantB).ForgotPin(new ForgotPinRequest(Harness.SharedPhone)));

        var filed = Assert.Single(h.AllResetRequests());
        Assert.Equal(h.SharedBId, filed.EmployeeId);
        Assert.Equal(TenantB, filed.TenantId);
    }

    // --- forgot-pin/verify from the app shell ------------------------------------------------------

    [Fact]
    public async Task App_shell_self_service_verify_resets_the_pin_and_audits_it_to_the_right_company()
    {
        using var h = new Harness();

        var result = Assert.IsType<OkObjectResult>(await h.AppShell().ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.AlicePhone, Harness.Fingerprint, SelfieBase64)));
        Assert.Contains("verified = True", result.Value!.ToString());

        var alice = h.Row(h.AliceId);
        Assert.NotEqual("hashed:1234", alice.PasswordHash);
        Assert.True(alice.MustChangePin);
        Assert.Equal(1, alice.TokenVersion);

        var audit = Assert.Single(h.AuditLogs());
        Assert.Equal(AuditEventType.PinResetSelfService, audit.EventType);
        Assert.Equal(TenantA, audit.TenantId);
    }

    [Fact]
    public async Task App_shell_verify_refuses_an_ambiguous_identifier_without_touching_any_pin()
    {
        using var h = new Harness();

        var result = Assert.IsType<OkObjectResult>(await h.AppShell().ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.SharedPhone, Harness.Fingerprint, SelfieBase64)));
        Assert.Equal("{ verified = False }", result.Value!.ToString());

        Assert.Equal("hashed:1234", h.Row(h.SharedAId).PasswordHash);
        Assert.Equal("hashed:1234", h.Row(h.SharedBId).PasswordHash);
        Assert.Empty(h.AuditLogs());
    }

    [Fact]
    public async Task App_shell_verify_answers_an_unknown_identifier_with_the_same_false()
    {
        using var h = new Harness();

        var result = Assert.IsType<OkObjectResult>(await h.AppShell().ForgotPinVerify(
            new ForgotPinVerifyRequest("994705559999", Harness.Fingerprint, SelfieBase64)));
        Assert.Equal("{ verified = False }", result.Value!.ToString());
        Assert.Empty(h.AuditLogs());
    }

    [Fact]
    public async Task App_shell_verify_still_demands_the_bound_device()
    {
        // Possession is the second factor; being tenant-less must not skip it.
        using var h = new Harness();

        var result = Assert.IsType<OkObjectResult>(await h.AppShell().ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.AlicePhone, "some-other-phone", SelfieBase64)));
        Assert.Equal("{ verified = False }", result.Value!.ToString());
        Assert.Equal("hashed:1234", h.Row(h.AliceId).PasswordHash);
    }

    [Fact]
    public async Task App_shell_verify_still_demands_a_face_above_the_self_service_bar()
    {
        // 94 clears the advisory check-in flag (85) but not the auth-grade bar (95).
        using var h = new Harness();

        var result = Assert.IsType<OkObjectResult>(await h.AppShell(faceScore: 94).ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.AlicePhone, Harness.Fingerprint, SelfieBase64)));
        Assert.Equal("{ verified = False }", result.Value!.ToString());
        Assert.Equal("hashed:1234", h.Row(h.AliceId).PasswordHash);
    }

    [Fact]
    public async Task App_shell_self_service_reset_also_clears_the_app_shells_own_lockout()
    {
        // Being locked out is the NORMAL way into this screen — eight mistyped PINs is what sends people
        // here. app-login gates on its own tenant-less namespace, so clearing only the web-login keys
        // would hand the employee a fresh PIN and then answer "Çox sayda cəhd" when they used it.
        using var h = new Harness();

        await h.AppShell().ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.AlicePhone, Harness.Fingerprint, SelfieBase64));

        Assert.Contains($"applogin:{LoginIdentity.LockoutKey(Guid.Empty, Harness.AlicePhone)}", h.Lockout.Cleared);
        Assert.Contains(LoginIdentity.LockoutKey(TenantA, Harness.AlicePhone), h.Lockout.Cleared);
    }

    [Fact]
    public async Task A_company_subdomain_verify_is_unchanged()
    {
        using var h = new Harness();

        var result = Assert.IsType<OkObjectResult>(await h.Subdomain(TenantA).ForgotPinVerify(
            new ForgotPinVerifyRequest(Harness.AlicePhone, Harness.Fingerprint, SelfieBase64)));
        Assert.Contains("verified = True", result.Value!.ToString());
        Assert.NotEqual("hashed:1234", h.Row(h.AliceId).PasswordHash);
        Assert.Equal(TenantA, Assert.Single(h.AuditLogs()).TenantId);
    }

    // --- "is this number known here?" — asked before the camera opens -----------

    private static bool KnownOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (bool)(ok.Value!.GetType().GetProperty("known")!.GetValue(ok.Value))!;
    }

    [Fact]
    public async Task Check_says_yes_for_a_number_that_is_here()
    {
        using var h = new Harness();
        Assert.True(KnownOf(await h.Subdomain(TenantA).ForgotPinCheck(new ForgotPinRequest(Harness.AlicePhone))));
    }

    [Fact]
    public async Task Check_says_no_for_a_number_that_is_not()
    {
        // The whole point: a typo is answered as a typo, instead of walking the person to a selfie that
        // was never going to match anybody.
        using var h = new Harness();
        Assert.False(KnownOf(await h.Subdomain(TenantA).ForgotPinCheck(new ForgotPinRequest("500000001"))));
    }

    [Fact]
    public async Task Check_answers_from_the_app_shell_too()
    {
        using var h = new Harness();
        Assert.True(KnownOf(await h.AppShell().ForgotPinCheck(new ForgotPinRequest(Harness.AlicePhone))));
    }

    [Fact]
    public async Task Check_says_nothing_about_another_companys_employee()
    {
        // Bahar is in tenant B. Asked on tenant A's subdomain she must not exist — the tenant boundary
        // is not softened by the convenience.
        using var h = new Harness();
        Assert.False(KnownOf(await h.Subdomain(TenantA).ForgotPinCheck(new ForgotPinRequest(Harness.BaharPhone))));
    }

    [Fact]
    public async Task An_empty_identifier_is_simply_unknown()
    {
        using var h = new Harness();
        Assert.False(KnownOf(await h.Subdomain(TenantA).ForgotPinCheck(new ForgotPinRequest(""))));
    }

    [Fact]
    public async Task Past_the_per_ip_cap_it_stops_telling_anyone_anything()
    {
        // A scraper cycling numbers burns the budget on misses and then gets "known" for everything,
        // so the answers stop being worth collecting — while a real employee still reaches the camera.
        using var h = new Harness();
        var controller = h.Subdomain(TenantA);
        for (var i = 0; i < 250; i++)
            await controller.ForgotPinCheck(new ForgotPinRequest($"5000{i:D5}"));

        Assert.True(KnownOf(await controller.ForgotPinCheck(new ForgotPinRequest("500000001"))));
    }

    [Fact]
    public async Task A_real_number_does_not_spend_the_budget()
    {
        // Charged only on misses: a site behind one router must not lock itself out by looking up its
        // own people.
        using var h = new Harness();
        var controller = h.Subdomain(TenantA);
        for (var i = 0; i < 300; i++)
            await controller.ForgotPinCheck(new ForgotPinRequest(Harness.AlicePhone));

        Assert.False(KnownOf(await controller.ForgotPinCheck(new ForgotPinRequest("500000002"))));
    }
}
