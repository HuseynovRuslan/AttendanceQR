using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
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
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILoginLockoutStore _lockoutStore;
    private readonly IPhotoStorageService _photoStorage;
    private readonly IFaceMatchService _faceMatch;
    private readonly IPushNotifier _pushNotifier;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthController> _logger;

    // Per-IP throttle for the anonymous forgot-pin endpoint: enough for a real person who taps it once
    // or twice, tight enough that a scripted sweep can't harvest timing samples or flood the queue.
    private const int MaxForgotPinPerWindow = 6;
    private static readonly TimeSpan ForgotPinWindow = TimeSpan.FromMinutes(15);

    // app-login is anonymous and spans every tenant, so identifier-rotation would otherwise give an
    // attacker unlimited PIN-spray + PBKDF2 work from one IP. Cap FAILURES per IP (a real login rarely
    // fails; a spray is all failures) — past the cap we 429 without doing the expensive verify.
    private const int MaxAppLoginFailPerIp = 30;
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

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex PinFormat();

    public AuthController(
        AppDbContext db, IPasswordHasher passwordHasher, IJwtService jwtService, ILoginLockoutStore lockoutStore,
        IPhotoStorageService photoStorage, IFaceMatchService faceMatch, IPushNotifier pushNotifier,
        IMemoryCache cache, ILogger<AuthController> logger)
    {
        _db = db;
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

        // 5b. The PIN is the account's only credential — enforce the 4-digit format here so it
        // can never be set to something weaker/different than what login expects.
        if (!PinFormat().IsMatch(request.Password))
            return BadRequest(new { error = "PinInvalid" });

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
            var remaining = _lockoutStore.RecordFailure(lockoutKey);
            if (remaining <= 0)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });
            return Unauthorized(new { error = "InvalidCredentials", remaining });
        }

        _lockoutStore.RecordSuccess(lockoutKey);
        return Ok(new { token = _jwtService.GenerateToken(employee!) });
    }

    // POST /api/auth/forgot-pin — an employee who forgot their PIN, and so cannot sign in, asks for a
    // reset from the login screen. Anonymous, like Login: the tenant is resolved from the subdomain by
    // middleware, so _db is already scoped. It only FILES a request into the admin queue — it resets
    // nothing and returns no PIN, so on its own it grants an attacker nothing.
    //
    // Always answers 200 with the same body whether or not the identifier matches an account, so the
    // RESPONSE reveals nothing. The one asymmetry left is timing (a real match does an extra write), so
    // the endpoint is throttled per IP: past the cap it returns the same 200 with no DB work at all,
    // which bounds both timing-sample harvesting (enumeration) and admin-queue flooding.
    [HttpPost("forgot-pin")]
    public async Task<IActionResult> ForgotPin([FromBody] ForgotPinRequest request)
    {
        // Per-IP throttle. Over the limit we no-op with the identical 200 — no lookup, no write — so it
        // neither reveals the throttle nor leaks anything about the identifier.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var throttleKey = $"forgotpin:{_db.CurrentTenantId:N}:{ip}";
        var seen = _cache.TryGetValue(throttleKey, out int n) ? n : 0;
        if (seen >= MaxForgotPinPerWindow)
            return Ok(new { ok = true });
        _cache.Set(throttleKey, seen + 1, ForgotPinWindow);

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        // Bound the work and give nothing away: an empty/oversized identifier just no-ops with the
        // same 200 as a miss.
        if (identifier.Length is > 0 and <= 200)
        {
            var phone = PhoneNumbers.Normalize(identifier);
            var employee = await _db.Employees.FirstOrDefaultAsync(e =>
                e.Email == identifier || (phone != null && e.PhoneNumber == phone));

            // Only a real, ALREADY-ACTIVATED account has a PIN to forget. A missing or un-activated one
            // silently no-ops (un-activated accounts are (re)invited, not reset). Same 200 either way.
            if (employee is { ActivatedAtUtc: not null })
            {
                // One open request per employee: a second tap — or a bored attacker cycling numbers —
                // can't flood the admin queue with duplicates.
                var hasPending = await _db.PinResetRequests
                    .AnyAsync(r => r.EmployeeId == employee.Id && r.Status == PinResetStatus.Pending);
                if (!hasPending)
                {
                    _db.PinResetRequests.Add(new PinResetRequest { EmployeeId = employee.Id });
                    _db.AuditLogs.Add(new AuditLog
                    {
                        EmployeeId = employee.Id,
                        EventType = AuditEventType.PinResetRequested,
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                    });
                    await _db.SaveChangesAsync();
                }
            }
        }

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
        var throttleKey = $"forgotpin:{_db.CurrentTenantId:N}:{ip}";
        var seen = _cache.TryGetValue(throttleKey, out int n) ? n : 0;
        if (seen >= MaxForgotPinPerWindow)
            return Ok(new { verified = false });
        _cache.Set(throttleKey, seen + 1, ForgotPinWindow);

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        var fingerprint = request.DeviceFingerprint?.Trim() ?? string.Empty;
        // Reject an oversized payload BEFORE decoding it or fetching the reference — base64 is ~1.33×
        // the bytes, so ~8M chars caps the decoded image near 6 MB.
        if (identifier.Length is 0 or > 200 || fingerprint.Length == 0
            || string.IsNullOrWhiteSpace(request.PhotoBase64) || request.PhotoBase64.Length > 8_000_000)
            return Ok(new { verified = false });

        var phone = PhoneNumbers.Normalize(identifier);
        var employee = await _db.Employees.FirstOrDefaultAsync(e =>
            e.Email == identifier || (phone != null && e.PhoneNumber == phone));

        // Cheap gates first — only spend a Rekognition call once possession (bound device) is proven.
        // Every failure below returns the identical false, so none of them is distinguishable.
        if (employee is not { ActivatedAtUtc: not null }
            || !_faceMatch.Enabled
            || string.IsNullOrEmpty(employee.ReferencePhotoKey))
            return Ok(new { verified = false });

        var deviceBound = await _db.DeviceBindings
            .AnyAsync(b => b.EmployeeId == employee.Id && b.IsActive && b.DeviceFingerprint == fingerprint);
        if (!deviceBound)
            return Ok(new { verified = false });

        // Per-account face-attempt cap (independent of IP, which an attacker can rotate): someone
        // holding the bound device must not get unlimited tries to fish for a photo/angle that clears
        // the bar. Over the cap, the self-service path is closed for a while and they use the admin queue.
        var faceLockKey = $"pinverify:{_db.CurrentTenantId:N}:{employee.Id:N}";
        var fails = _cache.TryGetValue(faceLockKey, out int f) ? f : 0;
        if (fails >= MaxFaceVerifyFailuresPerAccount)
            return Ok(new { verified = false });

        // Face match — the second factor. Any problem (no face, mismatch, crowd, storage/AWS error) is a
        // uniform false; nothing here can throw its way to a 500 that would leak a difference.
        FaceMatchOutcome outcome;
        try
        {
            var refBytes = await _photoStorage.GetBytesAsync(employee.ReferencePhotoKey, HttpContext.RequestAborted);
            var selfie = DecodeImage(request.PhotoBase64);
            if (selfie.Length is 0 or > 4 * 1024 * 1024)
                return Ok(new { verified = false });
            outcome = await _faceMatch.CompareAsync(refBytes, selfie, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Forgot-pin verify: face compare failed for {EmployeeId}", employee.Id);
            return Ok(new { verified = false });
        }

        // A single clear face whose similarity clears the HIGH self-service bar (NOT the advisory 85 the
        // check-in flag uses — this is a real auth factor). A near-miss counts against the per-account cap.
        if (outcome.Status != FaceMatchStatus.Ok || outcome.Score < ForgotPinFaceThreshold)
        {
            _cache.Set(faceLockKey, fails + 1, FaceVerifyLockWindow);
            return Ok(new { verified = false });
        }
        _cache.Remove(faceLockKey); // a genuine match clears the failure counter

        // Both factors passed — reset the PIN and hand it back, same reset an admin ResetPin does.
        var pin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
        employee.PasswordHash = _passwordHasher.Hash(pin);
        employee.MustChangePin = true;
        employee.TokenVersion++;
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employee.Id,
            EventType = AuditEventType.PinResetSelfService,
            IpAddress = ip
        });
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        var tenantId = _db.CurrentTenantId;
        _lockoutStore.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.Email));
        if (employee.PhoneNumber is not null)
            _lockoutStore.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.PhoneNumber));

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
        var employeeId = User.EmployeeId();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null)
            return Unauthorized(new { error = "InvalidToken" });

        if (!_passwordHasher.Verify(employee.PasswordHash, request.CurrentPassword))
            return Unauthorized(new { error = "InvalidCurrentPassword" });

        // Same 4-digit PIN format enforced at activation — a changed password must stay a PIN.
        if (!PinFormat().IsMatch(request.NewPassword))
            return BadRequest(new { error = "PinInvalid" });

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
        var employeeId = User.EmployeeId();

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null)
            return Unauthorized(new { error = "InvalidToken" });

        // Not on a temporary PIN — the employee must use change-password (which verifies the old PIN).
        if (!employee.MustChangePin)
            return Conflict(new { error = "AlreadySet" });

        if (!PinFormat().IsMatch(request.NewPin))
            return BadRequest(new { error = "PinInvalid" });

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
