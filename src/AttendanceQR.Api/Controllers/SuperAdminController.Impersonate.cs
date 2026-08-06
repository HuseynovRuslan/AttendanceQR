using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Impersonation — the support operator's "log in as this company's admin" for a short window, to see
// exactly what the customer sees. The minted token is confined to the target tenant + target admin
// (tid + tv), so it can never reach another company or escalate; it is short-lived so it cannot linger
// like a normal ~100-year login; and starting it is audited.
public partial class SuperAdminController
{
    private const int ImpersonationMinutes = 60;

    // POST /api/super/tenants/{id}/impersonate — mint a short-lived session as this company's founding
    // (oldest active) admin. Requires the company to be active and to have an admin.
    [HttpPost("tenants/{id:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid id)
    {
        if (!await CanAsync(OperatorPermission.Impersonate, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });
        if (!tenant.IsActive)
            return BadRequest(new { error = "TenantInactive" });

        var admin = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == id && e.Role == EmployeeRole.Admin && e.IsActive)
            .OrderBy(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (admin is null)
            return BadRequest(new { error = "NoAdmin" });

        var actorId = User.EmployeeId();
        // Impersonating your own account is a no-op that only muddies the audit — block it.
        if (admin.Id == actorId)
            return BadRequest(new { error = "CannotImpersonateSelf" });

        // Never impersonate a fellow operator: it would mint a token whose sub is an allowlisted id.
        // IsSuperAdmin already refuses impersonation tokens on /api/super, but keeping operators off the
        // impersonation surface entirely is the belt to that suspenders — and a cleaner audit trail.
        if (_superAdminIds.Contains(admin.Id))
            return BadRequest(new { error = "CannotImpersonateOperator" });

        var token = _jwt.GenerateImpersonationToken(admin, actorId, ImpersonationMinutes);
        await AuditAsync("ImpersonationStarted", tenant.Id, tenant.Slug,
            $"{admin.FullName} ({admin.PhoneNumber})", ct);

        return Ok(new
        {
            token,
            tenantSlug = tenant.Slug,
            tenantName = tenant.DisplayName,
            adminName = admin.FullName,
            expiresInMinutes = ImpersonationMinutes,
        });
    }
}
