using System.Security.Claims;
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
    private readonly ILogger<AuthController> _logger;

    // Computed once: a real hash to verify against when an email is unknown / has no password,
    // so login timing does not reveal whether an account exists.
    private static string? _decoyHash;

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex PinFormat();

    public AuthController(
        AppDbContext db, IPasswordHasher passwordHasher, IJwtService jwtService, ILoginLockoutStore lockoutStore,
        IPhotoStorageService photoStorage, ILogger<AuthController> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _lockoutStore = lockoutStore;
        _photoStorage = photoStorage;
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
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "TooManyAttempts" });

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

        // Identical response for every failure mode (unknown email, wrong password, inactive…).
        if (!canLogin)
        {
            _lockoutStore.RecordFailure(lockoutKey);
            return Unauthorized(new { error = "InvalidCredentials" });
        }

        _lockoutStore.RecordSuccess(lockoutKey);
        return Ok(new { token = _jwtService.GenerateToken(employee!) });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

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
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var employeeId))
            return Unauthorized(new { error = "InvalidToken" });

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

        return Ok(new { token = _jwtService.GenerateToken(employee) });
    }
}
