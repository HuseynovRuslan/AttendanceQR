using AttendanceQR.Domain;
using AttendanceQR.Infrastructure.Security;
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
        if (!await CanAsync(OperatorPermission.ManageUsers, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });
        var (emp, denied) = await SupportTargetAsync(id);
        if (emp is null)
            return denied!;

        var ct = HttpContext.RequestAborted;
        var pin = PinRules.Generate();
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
        if (!await CanAsync(OperatorPermission.ManageUsers, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });
        var (emp, denied) = await SupportTargetAsync(id);
        if (emp is null)
            return denied!;

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
        if (!await CanAsync(OperatorPermission.ManageUsers, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });
        var (emp, denied) = await SupportTargetAsync(id);
        if (emp is null)
            return denied!;

        var ct = HttpContext.RequestAborted;
        emp.TokenVersion++;
        await _db.SaveChangesAsync(ct);

        await AuditForUserAsync("UserSessionsRevoked", emp.TenantId, $"{emp.FullName} ({emp.PhoneNumber})", ct);
        return Ok(new { id = emp.Id });
    }

    /// <summary>
    /// The one gate for every support action on another person's account.
    ///
    /// It refuses a target that is itself an OPERATOR. Without that, the support surface was a
    /// privilege-escalation ladder: `reset-pin` hands back the plaintext PIN, so a Support-scoped
    /// operator could reset a FULL operator's PIN, read it out of the response and sign in as them —
    /// and the same three endpoints could also reactivate or log out a colleague. Nothing about the
    /// caller's own role changes that, so this refuses for everyone, including a Full operator:
    /// operator accounts are managed on the Team surface (which enforces WouldLeaveNoFull), not by
    /// minting each other's credentials. Impersonation already refused a fellow operator for exactly
    /// this reason (SuperAdminController.Impersonate.cs) — this is the same rule on the other door.
    ///
    /// Returns the error result to hand back so no caller can forget one: a non-operator caller gets
    /// 403 even for a missing id, so the endpoint cannot be used to probe who exists.
    /// </summary>
    private async Task<(Domain.Entities.Employee? Employee, IActionResult? Error)> SupportTargetAsync(Guid id)
    {
        if (!IsSuperAdmin)
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" }));

        var emp = await _db.Employees.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == id, HttpContext.RequestAborted);
        if (emp is null)
            return (null, NotFound(new { error = "EmployeeNotFound" }));
        if (_superAdminIds.Contains(emp.Id))
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error = "CannotManageOperator" }));

        return (emp, null);
    }

    // Small wrapper that resolves the target tenant's slug for a readable audit line.
    private async Task AuditForUserAsync(string action, Guid tenantId, string details, CancellationToken ct)
    {
        var slug = await _db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Slug).FirstOrDefaultAsync(ct);
        await AuditAsync(action, tenantId, slug, details, ct);
    }
}
