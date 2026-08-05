using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Cross-tenant employee support: find any person on the platform and perform the handful of actions a
// support operator needs — reset a forgotten PIN, unlock (reactivate) an account, or revoke all
// sessions. Every read here is IgnoreQueryFilters (this is the one place meant to cross tenants) and
// every write is audited. It deliberately CANNOT deactivate or delete — those belong to a tenant's own
// admin; the operator's job is to help a customer back in, not to lock them out.
public partial class SuperAdminController
{
    // GET /api/super/users?q=... — find employees across every company by name, phone or email. Needs at
    // least 2 characters so it never dumps the whole platform. Read-only.
    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string q = "", [FromQuery] int take = 50)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2)
            return Ok(Array.Empty<object>());

        var ct = HttpContext.RequestAborted;
        var pattern = $"%{term}%";

        var matches = await _db.Employees.IgnoreQueryFilters()
            .Where(e => EF.Functions.ILike(e.FullName, pattern)
                || (e.PhoneNumber != null && e.PhoneNumber.Contains(term))
                || (e.Email != null && EF.Functions.ILike(e.Email, pattern)))
            .OrderBy(e => e.FullName)
            .Take(Math.Clamp(take, 1, 100))
            .Select(e => new
            {
                e.Id, e.TenantId, e.FullName, e.PhoneNumber, e.Email, e.Role, e.IsActive, e.MustChangePin, e.LastActiveAtUtc,
            })
            .ToListAsync(ct);

        var tenantIds = matches.Select(m => m.TenantId).Distinct().ToList();
        var tenantMap = await _db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new { t.Slug, t.DisplayName }, ct);

        return Ok(matches.Select(m => new
        {
            id = m.Id,
            tenantId = m.TenantId,
            tenantSlug = tenantMap.GetValueOrDefault(m.TenantId)?.Slug,
            tenantName = tenantMap.GetValueOrDefault(m.TenantId)?.DisplayName,
            fullName = m.FullName,
            phone = m.PhoneNumber,
            email = m.Email,
            role = m.Role.ToString(),
            isActive = m.IsActive,
            mustChangePin = m.MustChangePin,
            lastActiveAtUtc = m.LastActiveAtUtc,
        }));
    }

    // POST /api/super/users/{id}/reset-pin — hand out a fresh temporary PIN and revoke the person's
    // existing sessions (TokenVersion bump). The plaintext is returned once and never stored; the
    // employee is forced to set their own PIN on next login (MustChangePin).
    [HttpPost("users/{id:guid}/reset-pin")]
    public async Task<IActionResult> ResetUserPin(Guid id)
    {
        var emp = await FindEmployeeAsync(id);
        if (emp is null)
            return IsSuperAdmin ? NotFound(new { error = "EmployeeNotFound" })
                                : StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var pin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
        emp.PasswordHash = _passwordHasher.Hash(pin);
        emp.MustChangePin = true;
        emp.TokenVersion++; // "log out everywhere" — the old token stops validating immediately.
        await _db.SaveChangesAsync(ct);

        await AuditForUserAsync("UserPinReset", emp.TenantId, $"{emp.FullName} ({emp.PhoneNumber})", ct);
        return Ok(new { id = emp.Id, tempPin = pin });
    }

    // POST /api/super/users/{id}/reactivate — turn an account back on (the admin kill-switch). This is
    // the fix for a tenant that locked its own sole admin out and went headless.
    [HttpPost("users/{id:guid}/reactivate")]
    public async Task<IActionResult> ReactivateUser(Guid id)
    {
        var emp = await FindEmployeeAsync(id);
        if (emp is null)
            return IsSuperAdmin ? NotFound(new { error = "EmployeeNotFound" })
                                : StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        emp.IsActive = true;
        await _db.SaveChangesAsync(ct);

        await AuditForUserAsync("UserReactivated", emp.TenantId, $"{emp.FullName} ({emp.PhoneNumber})", ct);
        return Ok(new { id = emp.Id, isActive = emp.IsActive });
    }

    // POST /api/super/users/{id}/revoke-sessions — invalidate every token the person holds without
    // touching their PIN. TokenVersion++ is the whole mechanism.
    [HttpPost("users/{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeUserSessions(Guid id)
    {
        var emp = await FindEmployeeAsync(id);
        if (emp is null)
            return IsSuperAdmin ? NotFound(new { error = "EmployeeNotFound" })
                                : StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        emp.TokenVersion++;
        await _db.SaveChangesAsync(ct);

        await AuditForUserAsync("UserSessionsRevoked", emp.TenantId, $"{emp.FullName} ({emp.PhoneNumber})", ct);
        return Ok(new { id = emp.Id });
    }

    // Loads a cross-tenant employee, or null when there is no such row OR the caller is not a
    // super-admin (the callers turn null into 404/403 so neither reveals the other).
    private async Task<Domain.Entities.Employee?> FindEmployeeAsync(Guid id)
    {
        if (!IsSuperAdmin)
            return null;
        return await _db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, HttpContext.RequestAborted);
    }

    // Small wrapper that resolves the target tenant's slug for a readable audit line.
    private async Task AuditForUserAsync(string action, Guid tenantId, string details, CancellationToken ct)
    {
        var slug = await _db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Slug).FirstOrDefaultAsync(ct);
        await AuditAsync(action, tenantId, slug, details, ct);
    }
}
