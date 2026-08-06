using AttendanceQR.Api.Contracts;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api.Controllers;

// Login for the platform operator console (admin.qrlog.az). The operator has no company subdomain, so —
// exactly like app-login — the account is found across every tenant by email/phone and the PIN is
// verified. The ONE extra gate is the super-admin allowlist. Kept as its own endpoint (not app-login +
// a client check) so a normal admin can never even reach the operator shell: the gate is server-side.
public partial class AuthController
{
    // Separate throttle namespace from app-login/web-login. A real operator login almost never fails, so
    // a run of failures from one source is a spray — cap it before the expensive cross-tenant verify.
    private const int MaxOperatorLoginFailPerIp = 20;
    private static readonly TimeSpan OperatorLoginIpWindow = TimeSpan.FromMinutes(15);

    [HttpPost("operator-login")]
    public async Task<IActionResult> OperatorLogin([FromBody] LoginRequest request, [FromServices] AppOptions options)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ipKey = $"oplogin-ip:{ip}";
        if ((_cache.TryGetValue(ipKey, out int ipFails) ? ipFails : 0) >= MaxOperatorLoginFailPerIp)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "TooManyAttempts", minutes = (int)OperatorLoginIpWindow.TotalMinutes });

        var identifier = request.Email?.Trim() ?? string.Empty;
        var lockoutKey = $"oplogin:{LoginIdentity.LockoutKey(Guid.Empty, identifier)}";
        if (_lockoutStore.IsLockedOut(lockoutKey))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });

        var phone = PhoneNumbers.Normalize(identifier);
        var decoy = _decoyHash ??= _passwordHasher.Hash("decoy-password-for-timing-parity");

        // Candidates across every tenant — same email/phone columns the web login matches on.
        var candidates = identifier.Length == 0
            ? new List<Employee>()
            : await _db.Employees.IgnoreQueryFilters()
                .Where(e => e.Email == identifier || (phone != null && e.PhoneNumber == phone))
                .ToListAsync();

        // Verify against each candidate (or a decoy when there are none) so timing never leaks account
        // existence. Success needs EXACTLY ONE active, activated, PIN-matching account.
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

        // The operator gate. A fully-verified account that is NOT on the allowlist fails IDENTICALLY to a
        // wrong PIN (same body, same failure accounting) — so this endpoint can't be turned into an
        // "is this account an operator?" oracle, and a normal admin who wanders onto admin.qrlog.az with
        // correct credentials simply cannot get in.
        var operatorIds = options.SuperAdminIdList();
        var isOperator = matched is not null && operatorIds.Contains(matched.Id);

        if (matched is null || matches != 1 || !isOperator)
        {
            _cache.Set(ipKey, (_cache.TryGetValue(ipKey, out int f) ? f : 0) + 1, OperatorLoginIpWindow);
            var remaining = _lockoutStore.RecordFailure(lockoutKey);
            if (remaining <= 0)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = "TooManyAttempts", minutes = _lockoutStore.LockoutMinutes });
            return Unauthorized(new { error = "InvalidCredentials", remaining });
        }

        _lockoutStore.RecordSuccess(lockoutKey);
        return Ok(new { token = _jwtService.GenerateToken(matched), employeeId = matched.Id });
    }
}
