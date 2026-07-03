using System.Text.RegularExpressions;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

[ApiController]
[Route("api/auth")]
public partial class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILoginLockoutStore _lockoutStore;

    // Computed once: a real hash to verify against when an email is unknown / has no password,
    // so login timing does not reveal whether an account exists.
    private static string? _decoyHash;

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex PinFormat();

    public AuthController(
        AppDbContext db, IPasswordHasher passwordHasher, IJwtService jwtService, ILoginLockoutStore lockoutStore)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _lockoutStore = lockoutStore;
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
            BoundAtUtc = now,
            IsActive = true
        });

        // 8. Mark activated and burn the token (single-use preserved).
        employee.ActivatedAtUtc = now;
        employee.InvitationTokenHash = null;
        employee.InvitationExpiresUtc = null;
        await _db.SaveChangesAsync();

        // 9. Hand back a login JWT so the employee is immediately usable.
        return Ok(new { token = _jwtService.GenerateToken(employee), employeeId = employee.Id });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // A 4-digit PIN is only 10,000 combinations — without this, an unthrottled attacker could
        // exhaust the whole space in seconds. Checked before touching the DB/hasher.
        if (_lockoutStore.IsLockedOut(request.Email))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "TooManyAttempts" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Email == request.Email);

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
            _lockoutStore.RecordFailure(request.Email);
            return Unauthorized(new { error = "InvalidCredentials" });
        }

        _lockoutStore.RecordSuccess(request.Email);
        return Ok(new { token = _jwtService.GenerateToken(employee!) });
    }
}
