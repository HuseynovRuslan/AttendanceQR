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

        var actorId = User.EmployeeId();

        // The company's admins, oldest first — then the first one that is actually somebody else's.
        //
        // It used to take the oldest and refuse if that happened to be an operator, which locked the
        // door on the very companies that need it: in a tenant an operator once set up by hand the
        // OLDEST admin IS the operator, so "Daxil ol" answered CannotImpersonateOperator and there was
        // no way in at all. Skipping past them reaches the customer's own admin — the account the
        // session should be borrowing anyway.
        //
        // The two skips are different rules and both still hold: an operator is skipped because
        // minting a token whose sub is an allowlisted id is what the takeover fix forbids, and self is
        // skipped because impersonating yourself only muddies the audit.
        var admins = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == id && e.Role == EmployeeRole.Admin && e.IsActive)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(ct);
        if (admins.Count == 0)
            return BadRequest(new { error = "NoAdmin" });

        var admin = admins.FirstOrDefault(a => a.Id != actorId && !_superAdminIds.Contains(a.Id));
        if (admin is null)
            // Admins exist, but every one of them is an operator (or this operator). Distinct from
            // NoAdmin: the company is not adminless, it just has nobody the console may borrow, so the
            // fix is to create the customer's own admin rather than to wonder where the admin went.
            return BadRequest(new { error = "NoImpersonableAdmin" });

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
