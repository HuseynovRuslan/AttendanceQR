using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AttendanceQR.Api.Controllers;

[ApiController]
[Route("api/auth")]
public partial class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILoginLockoutStore _lockoutStore;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IFaceMatchService _faceMatch;
    private readonly IPushNotifier _pushNotifier;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthController> _logger;

    // Per-IP throttle for the anonymous PIN-recovery endpoints — and "per IP" has to be read as very
    // much coarser than it sounds. Two things widen it: a whole factory sits behind one NAT, and
    // nothing in this API trusts X-Forwarded-For (there is no UseForwardedHeaders anywhere), so behind
    // Caddy every request arrives from the proxy container's own address. In production this bucket is
    // therefore closer to ONE for the whole platform than one per client. A cap sized as if it were
    // per-person would black out recovery for everybody — which is precisely the failure it exists to
    // prevent, because a mass lockout is when the most people need this path within one hour.
    //
    // Only requests that identified NOBODY are charged (see ForgotPin/ForgotPinVerify below). Probes,
    // whether enumerating identifiers or harvesting timings, are all misses by definition, so the cap
    // still bounds exactly what it was put there for, while the employee who really does own the
    // account never spends from a budget shared with everyone else.
    private const int MaxForgotPinPerWindow = 250;
    private static readonly TimeSpan ForgotPinWindow = TimeSpan.FromMinutes(15);

    // app-login is anonymous and spans every tenant, so identifier-rotation would otherwise give an
    // attacker unlimited PIN-spray + PBKDF2 work from one IP. Cap FAILURES per IP (a real login rarely
    // fails; a spray is all failures) — past the cap we 429 without doing the expensive verify.
    //
    // The cap is sized for the fact that one IP is a whole building: at 2000 employees behind one
    // corporate NAT, a handful of fat-fingered PINs at shift start must NOT black out login for
    // everyone (the old cap of 30 guaranteed exactly that during onboarding). 250 failures/15min is
    // still useless for spraying a 10,000-PIN space — per-identifier lockout limits any single
    // account regardless — and every SUCCESS from the IP pays a failure back, so a legitimately busy
    // NAT (many successes, few typos) never accumulates toward the cap at all.
    private const int MaxAppLoginFailPerIp = 250;
    private static readonly TimeSpan AppLoginIpWindow = TimeSpan.FromMinutes(15);

    // Self-service reset gates biometrically, so it needs a MUCH higher bar than the advisory check-in
    // flag (85): this is an auth factor, not a "looks suspicious" hint. And it caps face attempts PER
    // ACCOUNT (not just per IP, which rotates) so an attacker holding a bound device can't fish many
    // photos of the victim for one that clears the bar — after the cap it falls back to the admin queue.
    private const int ForgotPinFaceThreshold = 95;
    private const int MaxFaceVerifyFailuresPerAccount = 5;
    private static readonly TimeSpan FaceVerifyLockWindow = TimeSpan.FromMinutes(30);

    // Computed once: a real hash to verify against when an email is unknown / has no password,
    // so login timing does not reveal whether an account exists.
    private static string? _decoyHash;

    public AuthController(
        AppDbContext db, ITenantContext tenant, IPasswordHasher passwordHasher, IJwtService jwtService,
        ILoginLockoutStore lockoutStore, IPhotoStorageService photoStorage, IFaceMatchService faceMatch,
        IPushNotifier pushNotifier, IMemoryCache cache, ILogger<AuthController> logger)
    {
        _db = db;
        _tenant = tenant;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _lockoutStore = lockoutStore;
        _photoStorage = photoStorage;
        _faceMatch = faceMatch;
        _pushNotifier = pushNotifier;
        _cache = cache;
        _logger = logger;
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest request)
    {
        // 1. Parse the token into its (public) employee id and (secret) random part.
        if (!ActivationToken.TryParse(request.ActivationToken, out var employeeId, out var randomPart))
            return BadRequest(new { error = "InvalidToken" });

        // 2. Look the account up by id — a key that survives activation, unlike the token hash
        //    which is nulled on first use. This is what makes step 3 reachable.
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null)
            return BadRequest(new { error = "InvalidToken" });

        // 3. Single-use — already activated. Must come BEFORE the hash check below, because an
        //    activated account has a null InvitationTokenHash (the hash check would just fail).
        if (employee.ActivatedAtUtc is not null)
            return Conflict(new { error = "AlreadyActivated" });

        // 4. Expiry.
        if (employee.InvitationExpiresUtc is null || employee.InvitationExpiresUtc < DateTime.UtcNow)
            return BadRequest(new { error = "TokenExpired" });

        // 5. Verify the secret random part against the stored hash (constant-time, null-safe).
        if (!ActivationToken.VerifyRandomPart(randomPart, employee.InvitationTokenHash))
            return BadRequest(new { error = "InvalidToken" });

        // 5b. The PIN is the account's only credential — enforce the shape here so it can never be
        // set to something weaker/different than what login expects, and refuse the handful of
        // guesses an attacker starts with (0000, 1234, 1212).
        if (!PinRules.IsWellFormed(request.Password))
            return BadRequest(new { error = "PinInvalid" });
        if (PinRules.IsTooWeak(request.Password))
            return BadRequest(new { error = "PinTooWeak" });

        var now = DateTime.UtcNow;

        // 6. Set the password.
        employee.PasswordHash = _passwordHasher.Hash(request.Password);

        // 7. Bind the device at activation time (Variant 1).
        _db.DeviceBindings.Add(new DeviceBinding
        {
            EmployeeId = employee.Id,
            DeviceFingerprint = request.DeviceFingerprint,
            DeviceLabel = string.IsNullOrWhiteSpace(request.DeviceLabel) ? null : request.DeviceLabel.Trim(),
            BoundVia = DeviceBindingOrigin.Activation,
            BoundAtUtc = now,
            LastSeenAtUtc = now,
            IsActive = true
        });

        // 8. Mark activated and burn the token (single-use preserved).
        employee.ActivatedAtUtc = now;
        employee.InvitationTokenHash = null;
        employee.InvitationExpiresUtc = null;
        await _db.SaveChangesAsync();

        // 8b. Store the deliberate enrollment selfie as the reference photo (best-effort — a storage
        // failure must NOT fail activation). This is a far better reference than the silent
        // first-check-in fallback: the employee is looking at the camera on purpose.
        if (!string.IsNullOrWhiteSpace(request.PhotoBase64))
        {
            try
            {
                var bytes = DecodeImage(request.PhotoBase64);
                if (bytes.Length is > 0 and <= 2 * 1024 * 1024)
                {
                    employee.ReferencePhotoKey = await _photoStorage.UploadReferencePhotoAsync(
                        employee.Id, bytes, HttpContext.RequestAborted);
                    employee.ReferencePhotoTakenAtUtc = now;
                    await _db.SaveChangesAsync(HttpContext.RequestAborted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Activation: failed to store reference photo for {EmployeeId}", employee.Id);
            }
        }

        // 9. Hand back a login JWT so the employee is immediately usable.
        return Ok(new { token = _jwtService.GenerateToken(employee), employeeId = employee.Id });
    }

    // POST /api/auth/app-login — login for the single-URL native app, which has no company subdomain to
    // resolve the tenant from. Finds the employee across ALL companies by email/phone, verifies the PIN,
    // and issues a token for whichever company they belong to (the token's tid scopes every later
    // request). The subdomain web login above is untouched and stays strictly tenant-scoped.
    //
    // This is the ONE anonymous cross-tenant lookup: IgnoreQueryFilters is used deliberately, because
    // resolving the tenant from the credentials IS the job. It returns ONLY a token for a fully verified
    // account and never another company's data. Tenant-optional (see Program.cs) so it runs with no
    // resolved tenant; it never reads CurrentTenantId (no filtered query, no SaveChanges).
    [HttpPost("app-login")]
    public async Task<IActionResult> AppLogin([FromBody] LoginRequest request)
    {
        // Per-IP failure cap, checked BEFORE any DB/PBKDF2 work: bounds identifier-rotation spray and
        // the CPU cost of the cross-tenant verify from a single source. Over the cap → 429, no work.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ipKey = $"applogin-ip:{ip}";
        if ((_cache.TryGetValue(ipKey, out int ipFails) ? ipFails : 0) >= MaxAppLoginFailPerIp)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "TooManyAttempts", minutes = (int)AppLoginIpWindow.TotalMinutes });

        var identifier = request.Email?.Trim() ?? string.Empty;
        // Tenant-less lockout: this endpoint spans every company, so the brute-force budget is per
        // identifier only (a distinct namespace from the per-tenant web login lockout).
        var lockoutKey = $"applogin:{LoginIdentity.LockoutKey(Guid.Empty, identifier)}";
        if (_lockoutStore.IsLockedOut(lockoutKey))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });

        var phone = PhoneNumbers.Normalize(identifier);
        var decoy = _decoyHash ??= _passwordHasher.Hash("decoy-password-for-timing-parity");

        // Candidates across every tenant. Matched on the same email/phone columns the web login uses, so
        // it's a handful of rows at most (usually zero or one).
        var candidates = identifier.Length == 0
            ? new List<Employee>()
            : await _db.Employees.IgnoreQueryFilters()
                .Where(e => e.Email == identifier || (phone != null && e.PhoneNumber == phone))
                .ToListAsync();

        // Verify against each candidate (and a decoy when there are none) so timing doesn't leak whether
        // the identifier exists. A login succeeds only if EXACTLY ONE active, activated account's PIN
        // matches — an identifier+PIN that collides across two companies is ambiguous and rejected, not
        // guessed. Identical failure response for every case (unknown, wrong PIN, inactive, ambiguous).
        Employee? matched = null;
        var matches = 0;
        if (candidates.Count == 0)
        {
            _passwordHasher.Verify(decoy, request.Password);
        }
        else
        {
            foreach (var c in candidates)
            {
                var hash = string.IsNullOrEmpty(c.PasswordHash) ? decoy : c.PasswordHash;
                var ok = _passwordHasher.Verify(hash, request.Password)
                         && c.IsActive && c.ActivatedAtUtc is not null && !string.IsNullOrEmpty(c.PasswordHash);
                if (ok) { matched = c; matches++; }
            }
        }

        if (matched is null || matches != 1)
        {
            // Count this failure against the per-IP cap (successes don't count, so a legit user is
            // never throttled — only a stream of failures from one source is).
            _cache.Set(ipKey, (_cache.TryGetValue(ipKey, out int f) ? f : 0) + 1, AppLoginIpWindow);
            var remaining = _lockoutStore.RecordFailure(lockoutKey);
            if (remaining <= 0)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });
            return Unauthorized(new { error = "InvalidCredentials", remaining });
        }

        _lockoutStore.RecordSuccess(lockoutKey);
        // A success pays one failure back: a shared NAT with real users on it self-heals instead of
        // creeping toward the cap on typos alone.
        if (_cache.TryGetValue(ipKey, out int paid) && paid > 0)
            _cache.Set(ipKey, paid - 1, AppLoginIpWindow);
        return Ok(new { token = _jwtService.GenerateToken(matched), employeeId = matched.Id });
    }

    // Accepts a data URL ("data:image/jpeg;base64,AAAA…") or a bare base64 string.
    private static byte[] DecodeImage(string input)
    {
        var comma = input.IndexOf(',');
        var b64 = input.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? input[(comma + 1)..]
            : input;
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Per-IP failure cap, checked BEFORE any DB/PBKDF2 work. The per-identifier lockout below
        // bounds guessing at ONE account — it does nothing against the attack that actually works
        // here: spraying a single obvious PIN across every phone number in the company. Each
        // identifier gets its own fresh budget, so 114 employees is 114 free attempts and no lockout
        // ever trips. This is the cap that costs. Same window and namespace shape as app-login.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ipKey = $"login-ip:{ip}";
        if ((_cache.TryGetValue(ipKey, out int ipFails) ? ipFails : 0) >= MaxAppLoginFailPerIp)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "TooManyAttempts", minutes = (int)AppLoginIpWindow.TotalMinutes });

        // A 4-digit PIN is only 10,000 combinations — without this, an unthrottled attacker could
        // exhaust the whole space in seconds. Checked before touching the DB/hasher, so the key has
        // to be derived from the input alone — LoginIdentity canonicalizes it the same way the
        // account lookup below does, so every spelling of one number spends ONE budget.
        var lockoutKey = LoginIdentity.LockoutKey(_db.CurrentTenantId, request.Email);
        if (_lockoutStore.IsLockedOut(lockoutKey))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });

        // The identifier field carries an email OR a phone number — match either.
        var identifier = request.Email?.Trim() ?? string.Empty;
        var phone = PhoneNumbers.Normalize(identifier);
        var employee = await _db.Employees.FirstOrDefaultAsync(e =>
            e.Email == identifier || (phone != null && e.PhoneNumber == phone));

        // Always perform a verification — against a decoy hash when the account is unknown or
        // has no password yet — so timing doesn't leak account existence (email enumeration).
        var decoy = _decoyHash ??= _passwordHasher.Hash("decoy-password-for-timing-parity");
        var hashToCheck = string.IsNullOrEmpty(employee?.PasswordHash) ? decoy : employee!.PasswordHash;
        var passwordOk = _passwordHasher.Verify(hashToCheck, request.Password);

        var canLogin = employee is not null
                       && employee.IsActive
                       && employee.ActivatedAtUtc is not null
                       && passwordOk;

        // Identical response for every failure mode (unknown email, wrong password, inactive…) — the
        // remaining-attempts count leaks nothing about which, only how close THIS bucket is to a lock,
        // which the attacker could count anyway. It lets a real employee see "2 tries left" before the
        // cool-off, instead of being surprised by it.
        if (!canLogin)
        {
            // Only failures count, so a real employee signing in normally is never throttled — the
            // cap only bites a stream of failures from one source.
            _cache.Set(ipKey, (_cache.TryGetValue(ipKey, out int f) ? f : 0) + 1, AppLoginIpWindow);
            var remaining = _lockoutStore.RecordFailure(lockoutKey);
            if (remaining <= 0)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });
            return Unauthorized(new { error = "InvalidCredentials", remaining });
        }

        _lockoutStore.RecordSuccess(lockoutKey);
        // Same NAT self-healing as app-login: a success pays one failure back.
        if (_cache.TryGetValue(ipKey, out int paid) && paid > 0)
            _cache.Set(ipKey, paid - 1, AppLoginIpWindow);
        return Ok(new { token = _jwtService.GenerateToken(employee!) });
    }

    // The per-IP throttle namespace for the PIN-recovery endpoints. It has to be computed BEFORE any
    // lookup — the whole point of the cap is that an over-limit call does no DB work at all — so when
    // the caller has no subdomain there is no account yet to take a tenant from, and the bucket is the
    // tenant-less "app" one. Same shape as app-login's tenant-less lockout namespace: one company's
    // employees can never spend another company's budget, and the app shell's callers share their own.
    private string PinRecoveryThrottleScope() =>
        _tenant.IsResolved ? _db.CurrentTenantId.ToString("N") : "app";

    /// <summary>
    /// Every account a PIN-recovery identifier could mean.
    ///
    /// On a company subdomain the middleware has already resolved the tenant, so this is exactly the
    /// filtered lookup these endpoints have always done and nothing about their behaviour changes.
    ///
    /// From the single-URL native app shell (app.qrlog.az) there is no subdomain — and an employee who
    /// has forgotten their PIN has no token either, so the identifier they type is the only thing left
    /// to attribute the request with. So, exactly like app-login, the account is looked up across every
    /// company with IgnoreQueryFilters and the tenant is taken FROM the matched row.
    ///
    /// The cross-tenant side applies app-login's candidate rule exactly — LIVE and activated. A number
    /// left behind by someone who moved between two of our tenants (three companies hiring from one
    /// labour market: ordinary, not a corner case) must not shadow the person using it today.
    /// </summary>
    private async Task<List<Employee>> ResolvePinRecoveryCandidatesAsync(string identifier, CancellationToken ct)
    {
        var phone = PhoneNumbers.Normalize(identifier);

        if (_tenant.IsResolved)
        {
            var scoped = await _db.Employees.FirstOrDefaultAsync(
                e => e.Email == identifier || (phone != null && e.PhoneNumber == phone), ct);
            return scoped is null ? new List<Employee>() : new List<Employee> { scoped };
        }

        var candidates = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Email == identifier || (phone != null && e.PhoneNumber == phone))
            .ToListAsync(ct);

        return candidates.Where(c => c.IsActive && c.ActivatedAtUtc is not null).ToList();
    }

    /// <summary>
    /// The ONE account behind a recovery identifier, or null when that cannot be decided.
    ///
    /// This is the rule for the path that hands a credential back: an identifier living in two
    /// companies is AMBIGUOUS and refused, never guessed, because there is no PIN here to disambiguate
    /// on the way app-login does. The caller answers a null exactly as it answers a miss, so nothing
    /// distinguishes the two. (forgot-pin, which returns no credential, is deliberately more generous
    /// — see there.)
    ///
    /// The matched row's tenant is resolved into the request immediately, so everything after this
    /// point — queries, inserts, the push alert — runs under the normal fail-closed filter instead of
    /// tenant-less.
    /// </summary>
    private async Task<Employee?> ResolvePinRecoveryAccountAsync(string identifier, CancellationToken ct)
    {
        var candidates = await ResolvePinRecoveryCandidatesAsync(identifier, ct);
        if (candidates.Count != 1)
            return null;

        _tenant.Resolve(candidates[0].TenantId);
        return candidates[0];
    }

    // POST /api/auth/forgot-pin — an employee who forgot their PIN, and so cannot sign in, asks for a
    // reset from the login screen. Anonymous, like Login: on a company subdomain the tenant is resolved
    // from the host by middleware, so _db is already scoped; from the native app shell there is no
    // subdomain and the account itself names the company (see ResolvePinRecoveryCandidatesAsync). It only
    // FILES a request into the admin queue — it resets nothing and returns no PIN, so on its own it
    // grants an attacker nothing.
    //
    // Always answers 200 with the same body whether or not the identifier matches an account, so the
    // RESPONSE reveals nothing. The one asymmetry left is timing (a real match does an extra write), so
    // the endpoint is throttled per IP: past the cap it returns the same 200 with no DB work at all,
    // which bounds both timing-sample harvesting (enumeration) and admin-queue flooding.
    // POST /api/auth/forgot-pin/check — does this number belong to anybody here?
    //
    // The screen used to walk everyone to the selfie and answer every failure the same way, so a
    // mistyped digit and a face that did not match were indistinguishable — people retook the photo
    // five times for a typo. This says which it is, before the camera opens.
    //
    // It does reveal whether an identifier exists, which the rest of this controller deliberately never
    // does. The trade was made with eyes open: recovery is for people who are already stuck, the
    // alternative is a dead end they cannot read, and the leak is bounded three ways — the answer costs
    // an attempt from the same per-IP budget the reset flow uses, past the cap it always answers
    // "known" (so a scraper learns nothing and the employee still reaches the camera), and it says
    // nothing about WHO: no name, no company, no role.
    [HttpPost("forgot-pin/check")]
    public async Task<IActionResult> ForgotPinCheck([FromBody] ForgotPinRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var throttleKey = $"forgotpin:{PinRecoveryThrottleScope()}:{ip}";
        var seen = _cache.TryGetValue(throttleKey, out int n) ? n : 0;
        if (seen >= MaxForgotPinPerWindow)
            // Out of budget: answer as if it were known. The employee carries on to the camera (where a
            // real account still verifies), and somebody cycling numbers is told nothing.
            return Ok(new { known = true });

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        var candidates = identifier.Length is > 0 and <= 200
            ? await ResolvePinRecoveryCandidatesAsync(identifier, HttpContext.RequestAborted)
            : new List<Employee>();
        var known = candidates.Any(c => c.ActivatedAtUtc is not null);

        // Charged only on a miss — the same rule the reset endpoint uses, for the same reason: an
        // enumeration probe is all misses, an employee's own number is not.
        if (!known)
            _cache.Set(throttleKey, seen + 1, ForgotPinWindow);

        return Ok(new { known });
    }

    [HttpPost("forgot-pin")]
    public async Task<IActionResult> ForgotPin([FromBody] ForgotPinRequest request)
    {
        // Per-IP throttle. Over the limit we no-op with the identical 200 — no lookup, no write — so it
        // neither reveals the throttle nor leaks anything about the identifier.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var throttleKey = $"forgotpin:{PinRecoveryThrottleScope()}:{ip}";
        var seen = _cache.TryGetValue(throttleKey, out int n) ? n : 0;
        if (seen >= MaxForgotPinPerWindow)
            return Ok(new { ok = true });

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        // Bound the work and give nothing away: an empty/oversized identifier just no-ops with the
        // same 200 as a miss.
        var candidates = identifier.Length is > 0 and <= 200
            ? await ResolvePinRecoveryCandidatesAsync(identifier, HttpContext.RequestAborted)
            : new List<Employee>();

        // Only a real, ALREADY-ACTIVATED account has a PIN to forget. A missing or un-activated one
        // silently no-ops (un-activated accounts are (re)invited, not reset). Same 200 either way.
        var targets = candidates.Where(c => c.ActivatedAtUtc is not null).ToList();

        // The throttle is charged HERE, only when the identifier named nobody. Someone cycling numbers
        // to find out which exist gets nothing but misses and still runs into the cap; the employee who
        // typed their own number does not spend a budget that, behind the proxy, everyone shares.
        if (targets.Count == 0)
            _cache.Set(throttleKey, seen + 1, ForgotPinWindow);

        // Unlike verify below, this hands back no credential — it only files a plea into an admin queue
        // and a human identifies their own employee out of band. So an identifier that two companies
        // both use is NOT the dead end it has to be there: file into each of them. Refusing would leave
        // that employee reading "Sorğu göndərildi" forever with nothing behind it, and the response body
        // is the same single generic 200 either way.
        var filed = false;
        foreach (var employee in targets)
        {
            // One open request per employee: a second tap — or a bored attacker cycling numbers —
            // can't flood the admin queue with duplicates. IgnoreQueryFilters because an app-shell call
            // has no ambient tenant for the filter to read (it would throw) and the targets can belong
            // to different companies. It widens nothing: EmployeeId is a global key, so this returns
            // exactly the rows the filtered query returned on a subdomain.
            var hasPending = await _db.PinResetRequests.IgnoreQueryFilters()
                .AnyAsync(r => r.EmployeeId == employee.Id && r.Status == PinResetStatus.Pending,
                    HttpContext.RequestAborted);
            if (hasPending)
                continue;

            // TenantId comes from the ACCOUNT, not from the ambient tenant. This row is the only trace
            // the employee's plea leaves — if it were stamped with the wrong company (or, from the
            // tenant-less app shell, with nothing at all) it would never appear in /admin/pin-resets and
            // the request would silently evaporate. SaveChanges' stamping only fills rows whose TenantId
            // is still empty, so setting it here always wins; on a subdomain it is the same value
            // stamping would have written, and with no tenant at all it is what keeps the save from
            // throwing.
            _db.PinResetRequests.Add(new PinResetRequest
            {
                EmployeeId = employee.Id,
                TenantId = employee.TenantId
            });
            _db.AuditLogs.Add(new AuditLog
            {
                EmployeeId = employee.Id,
                TenantId = employee.TenantId,
                EventType = AuditEventType.PinResetRequested,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });
            filed = true;
        }

        if (filed)
            await _db.SaveChangesAsync(HttpContext.RequestAborted);

        return Ok(new { ok = true });
    }

    // POST /api/auth/forgot-pin/verify — SELF-SERVICE PIN reset with no admin in the loop. Identity is
    // proven by TWO factors: (1) a live selfie that matches the account's reference photo, and (2) a
    // device already bound to that account — so a stray photo of an employee is not enough on its own
    // (you also need their bound phone). On success a fresh temp PIN is returned on the spot; on ANY
    // failure the response is a uniform { verified: false } and the client offers the admin-queue path.
    [HttpPost("forgot-pin/verify")]
    public async Task<IActionResult> ForgotPinVerify([FromBody] ForgotPinVerifyRequest request)
    {
        // Same per-IP throttle as forgot-pin: bounds brute-forcing the face check and enumeration.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var throttleKey = $"forgotpin:{PinRecoveryThrottleScope()}:{ip}";
        var seen = _cache.TryGetValue(throttleKey, out int n) ? n : 0;
        if (seen >= MaxForgotPinPerWindow)
            return Ok(new { verified = false });

        // Charged on the way OUT of a failure, never on a verified reset — same reasoning as forgot-pin:
        // every probe is a failure, so the cap still bounds enumeration and face brute-forcing, while
        // someone who actually clears the face check cannot drain a bucket the platform shares. What
        // bounds an attacker fishing for one good photo of one victim is the per-ACCOUNT cap below, and
        // that one is unaffected by this.
        IActionResult Miss()
        {
            _cache.Set(throttleKey, (_cache.TryGetValue(throttleKey, out int charged) ? charged : 0) + 1,
                ForgotPinWindow);
            return Ok(new { verified = false });
        }

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        var fingerprint = request.DeviceFingerprint?.Trim() ?? string.Empty;
        // Reject an oversized payload BEFORE decoding it or fetching the reference — base64 is ~1.33×
        // the bytes, so ~8M chars caps the decoded image near 6 MB.
        if (identifier.Length is 0 or > 200 || fingerprint.Length == 0
            || string.IsNullOrWhiteSpace(request.PhotoBase64) || request.PhotoBase64.Length > 8_000_000)
            return Miss();

        // Same resolution as forgot-pin: tenant-scoped on a subdomain, across every company (and
        // ambiguity-refusing) from the app shell. A null here is indistinguishable from every other
        // failure below — they all return the same false.
        var employee = await ResolvePinRecoveryAccountAsync(identifier, HttpContext.RequestAborted);

        // Cheap gates first — only spend a Rekognition call once possession (bound device) is proven.
        // Every failure below returns the identical false, so none of them is distinguishable.
        if (employee is not { ActivatedAtUtc: not null }
            || !_faceMatch.Enabled
            || string.IsNullOrEmpty(employee.ReferencePhotoKey))
            return Miss();

        var deviceBound = await _db.DeviceBindings
            .AnyAsync(b => b.EmployeeId == employee.Id && b.IsActive && b.DeviceFingerprint == fingerprint);
        if (!deviceBound)
            return Miss();

        // Per-account face-attempt cap (independent of IP, which an attacker can rotate): someone
        // holding the bound device must not get unlimited tries to fish for a photo/angle that clears
        // the bar. Over the cap, the self-service path is closed for a while and they use the admin queue.
        // Keyed on the ACCOUNT's tenant, so the cap follows the employee whether they came in through
        // their company subdomain or through the tenant-less app shell — otherwise the app shell would
        // hand the same account a second, independent budget of face attempts.
        var faceLockKey = $"pinverify:{employee.TenantId:N}:{employee.Id:N}";
        var fails = _cache.TryGetValue(faceLockKey, out int f) ? f : 0;
        if (fails >= MaxFaceVerifyFailuresPerAccount)
            return Miss();

        // Face match — the second factor. Any problem (no face, mismatch, crowd, storage/AWS error) is a
        // uniform false; nothing here can throw its way to a 500 that would leak a difference.
        FaceMatchOutcome outcome;
        try
        {
            var refBytes = await _photoStorage.GetBytesAsync(employee.ReferencePhotoKey, HttpContext.RequestAborted);
            var selfie = DecodeImage(request.PhotoBase64);
            if (selfie.Length is 0 or > 4 * 1024 * 1024)
                return Miss();
            outcome = await _faceMatch.CompareAsync(refBytes, selfie, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Forgot-pin verify: face compare failed for {EmployeeId}", employee.Id);
            return Miss();
        }

        // A single clear face whose similarity clears the HIGH self-service bar (NOT the advisory 85 the
        // check-in flag uses — this is a real auth factor). A near-miss counts against the per-account cap.
        if (outcome.Status != FaceMatchStatus.Ok || outcome.Score < ForgotPinFaceThreshold)
        {
            _cache.Set(faceLockKey, fails + 1, FaceVerifyLockWindow);
            return Miss();
        }
        _cache.Remove(faceLockKey); // a genuine match clears the failure counter

        // Both factors passed — reset the PIN and hand it back, same reset an admin ResetPin does.
        var pin = PinRules.Generate();
        employee.PasswordHash = _passwordHasher.Hash(pin);
        employee.MustChangePin = true;
        employee.TokenVersion++;
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employee.Id,
            TenantId = employee.TenantId,
            EventType = AuditEventType.PinResetSelfService,
            IpAddress = ip
        });
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        // The account's own company — the lockout keys must match the ones Login/app-login spend, and
        // an app-shell caller has no ambient tenant to read.
        var tenantId = employee.TenantId;
        _lockoutStore.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.Email));
        if (employee.PhoneNumber is not null)
            _lockoutStore.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.PhoneNumber));

        // ...and the tenant-less namespace app-login gates on, which is a DIFFERENT key (Guid.Empty).
        // Arriving here already locked out is the normal case, not the rare one: mistyping a PIN eight
        // times is exactly what makes someone open "PIN-i unutdum". Without this they read the fresh PIN
        // off the screen, tap "Girişə keç", and the app answers "Çox sayda cəhd" — and their natural
        // reaction is to run the whole recovery flow again. Proving a face match is a strictly stronger
        // success signal than the PIN entry that clears the keys above, so it clears these too.
        _lockoutStore.RecordSuccess($"applogin:{LoginIdentity.LockoutKey(Guid.Empty, employee.Email)}");
        if (employee.PhoneNumber is not null)
            _lockoutStore.RecordSuccess($"applogin:{LoginIdentity.LockoutKey(Guid.Empty, employee.PhoneNumber)}");
        // And pay one failure back into the per-IP budget, exactly as a successful app-login does.
        var appLoginIpKey = $"applogin-ip:{ip}";
        if (_cache.TryGetValue(appLoginIpKey, out int appIpFails) && appIpFails > 0)
            _cache.Set(appLoginIpKey, appIpFails - 1, AppLoginIpWindow);

        // Out-of-band alert so a fraudulent self-service reset is visible to the real employee straight
        // away, even though this same call just logged them out. Best-effort — never fail the reset if
        // push is unavailable (the account is already reset by here).
        try
        {
            await _pushNotifier.NotifyEmployeesAsync(
                new[] { employee.Id },
                "PIN sıfırlandı",
                "PIN-iniz özünə-xidmət (üz təsdiqi) ilə sıfırlandı. Siz deyildinizsə, dərhal administratora müraciət edin.",
                "/login",
                HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Forgot-pin verify: reset notification failed for {EmployeeId}", employee.Id);
        }

        return Ok(new { verified = true, pin });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        // Same reason as set-initial-pin: an impersonation session must not be able to change the
        // password of the account it is borrowing.
        if (User.IsImpersonating())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });

        var employeeId = User.EmployeeId();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null)
            return Unauthorized(new { error = "InvalidToken" });

        if (!_passwordHasher.Verify(employee.PasswordHash, request.CurrentPassword))
            return Unauthorized(new { error = "InvalidCurrentPassword" });

        // Same rules as activation — a changed password must stay a PIN, and must not be a guess.
        if (!PinRules.IsWellFormed(request.NewPassword))
            return BadRequest(new { error = "PinInvalid" });
        if (PinRules.IsTooWeak(request.NewPassword))
            return BadRequest(new { error = "PinTooWeak" });

        employee.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        employee.MustChangePin = false;

        // Invalidate every other outstanding token (see Program.cs OnTokenValidated) — the token
        // returned below embeds the new version, so only THIS session survives the change.
        employee.TokenVersion++;
        await _db.SaveChangesAsync();

        return Ok(new { token = _jwtService.GenerateToken(employee) });
    }

    // POST /api/auth/set-initial-pin — first-time PIN set for an account still on a temporary PIN
    // (bulk import or an admin PIN reset). The employee has just signed in with the temp PIN, so no
    // current PIN is asked for; the server only allows this while MustChangePin is set, so it can't be
    // used to change a PIN without knowing the old one.
    [HttpPost("set-initial-pin")]
    [Authorize]
    public async Task<IActionResult> SetInitialPin([FromBody] SetInitialPinRequest request)
    {
        // Not from an impersonation session. The operator holds a token for the customer's admin while
        // setting their company up, and that admin is still on the temporary PIN handed over to them —
        // so this endpoint would let the operator burn the credential the customer has not used yet,
        // and walk away with a normal, never-expiring token for someone else's admin. The PIN is the
        // customer's to choose; the operator's session ends at the hour.
        if (User.IsImpersonating())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });

        var employeeId = User.EmployeeId();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null)
            return Unauthorized(new { error = "InvalidToken" });

        // Not on a temporary PIN — the employee must use change-password (which verifies the old PIN).
        if (!employee.MustChangePin)
            return Conflict(new { error = "AlreadySet" });

        if (!PinRules.IsWellFormed(request.NewPin))
            return BadRequest(new { error = "PinInvalid" });
        if (PinRules.IsTooWeak(request.NewPin))
            return BadRequest(new { error = "PinTooWeak" });

        employee.PasswordHash = _passwordHasher.Hash(request.NewPin);
        employee.MustChangePin = false;
        // The temp-PIN token(s) stop working; only the freshly issued token below survives.
        employee.TokenVersion++;
        await _db.SaveChangesAsync();

        // Only a brand-new account (bulk-imported, never enrolled a face) needs the reference-selfie
        // step after setting its PIN. An EXISTING employee who just had their PIN reset already has a
        // reference photo — re-capturing it here would overwrite a good baseline for no reason, so the
        // client skips that step when this is false.
        var needsReferencePhoto = string.IsNullOrEmpty(employee.ReferencePhotoKey);

        return Ok(new { token = _jwtService.GenerateToken(employee), needsReferencePhoto });
    }
}
