using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
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

    /// <summary>
    /// Who this console may borrow inside one company — its active admins and managers.
    ///
    /// Managers are here because half the support calls are about a manager's screen: a branch gate,
    /// an empty employee list, a leave they cannot file. Borrowing the admin to answer them shows the
    /// wrong screen — the admin sees everything, which is exactly what the caller does not.
    ///
    /// Plain employees are deliberately absent, and that is not squeamishness: a borrowed worker
    /// session can create ATTENDANCE. A support login must never be able to put a day's work on
    /// somebody's record.
    /// </summary>
    [HttpGet("tenants/{id:guid}/impersonation-targets")]
    public async Task<IActionResult> ImpersonationTargets(Guid id)
    {
        if (!await CanAsync(OperatorPermission.Impersonate, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var actorId = User.EmployeeId();

        var people = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == id && e.IsActive
                        && (e.Role == EmployeeRole.Admin || e.Role == EmployeeRole.Manager))
            .OrderBy(e => e.Role == EmployeeRole.Admin ? 0 : 1)
            .ThenBy(e => e.CreatedAtUtc)
            .Select(e => new { e.Id, e.FullName, e.Role, e.CreatedAtUtc })
            .ToListAsync(ct);

        // Same two skips as the mint below, applied here so the list never offers a door that then
        // refuses to open.
        people = people.Where(e => e.Id != actorId && !_superAdminIds.Contains(e.Id)).ToList();

        // Which branches a manager would actually see. Without it the list is a row of names and the
        // operator has to guess which manager is the one the caller is describing.
        var managerIds = people.Where(e => e.Role == EmployeeRole.Manager).Select(e => e.Id).ToList();
        var branches = managerIds.Count == 0
            ? new Dictionary<Guid, List<string>>()
            : (await _db.ManagedLocations.IgnoreQueryFilters()
                    .Where(m => managerIds.Contains(m.EmployeeId))
                    .Join(_db.Locations.IgnoreQueryFilters(), m => m.LocationId, l => l.Id,
                          (m, l) => new { m.EmployeeId, l.Name })
                    .ToListAsync(ct))
                .GroupBy(x => x.EmployeeId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Name).OrderBy(n => n).ToList());

        return Ok(people.Select(e => new
        {
            id = e.Id,
            fullName = e.FullName,
            role = e.Role.ToString(),
            branches = branches.GetValueOrDefault(e.Id, new List<string>()),
        }));
    }

    // POST /api/super/tenants/{id}/impersonate — mint a short-lived session inside this company.
    // Without employeeId it borrows the founding (oldest active) admin, which is what the button did
    // before a target could be chosen; with one it borrows that person, admin or manager.
    [HttpPost("tenants/{id:guid}/impersonate")]
    public async Task<IActionResult> Impersonate(Guid id, [FromQuery] Guid? employeeId = null)
    {
        if (!await CanAsync(OperatorPermission.Impersonate, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        return await StartSessionAsync(id, employeeId, readOnly: false);
    }

    // POST /api/super/tenants/{id}/view — the same session, with the writes taken off it.
    //
    // For «Qrup rəhbəri»: the customer's group head reads any of his companies through the company's
    // OWN admin screens — the today board, the reports, the tabel — and changes nothing. Reusing the
    // real panel is the point; rebuilding read-only copies of thirty-four screens in the operator
    // console would be a second implementation of every rule, drifting from the first.
    //
    // Gated only on being an operator, deliberately: reads are already open to every operator role,
    // and this mints strictly LESS than the impersonation above. The teeth are in the token — see
    // ViewOnlyBoundary, which refuses every mutating request the session makes.
    [HttpPost("tenants/{id:guid}/view")]
    public async Task<IActionResult> ViewTenant(Guid id, [FromQuery] Guid? employeeId = null)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        return await StartSessionAsync(id, employeeId, readOnly: true);
    }

    /// <summary>
    /// Mints a session inside one company. Every refusal below is shared by both doors on purpose: a
    /// read-only session must not be able to reach a seat the impersonation door would have refused —
    /// an inactive company, a plain employee's seat, an operator's own account.
    /// </summary>
    private async Task<IActionResult> StartSessionAsync(Guid id, Guid? employeeId, bool readOnly)
    {
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
        Employee? admin;

        if (employeeId is Guid chosen)
        {
            // An explicitly chosen target — the same three refusals as the automatic path, checked
            // one at a time so the console can say which one it hit rather than "not found".
            admin = await _db.Employees.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == chosen && e.TenantId == id && e.IsActive, ct);
            if (admin is null)
                return BadRequest(new { error = "TargetNotFound" });

            // The ceiling on WHOSE session may be borrowed. A manager is a smaller session than an
            // admin, so widening to them takes nothing away; a plain employee would be a session that
            // can record ATTENDANCE, and a support login must never be able to put a day's work on
            // somebody's record.
            if (admin.Role is not (EmployeeRole.Admin or EmployeeRole.Manager))
                return BadRequest(new { error = "TargetNotImpersonable" });

            // Unchanged from the automatic path and for the same reasons: minting a token whose sub is
            // an allowlisted id is what the 2026-08 takeover fix forbids, and borrowing yourself only
            // muddies the audit.
            if (_superAdminIds.Contains(admin.Id))
                return BadRequest(new { error = "CannotImpersonateOperator" });
            if (admin.Id == actorId)
                return BadRequest(new { error = "CannotImpersonateSelf" });
        }
        else
        {
            var admins = await _db.Employees.IgnoreQueryFilters()
                .Where(e => e.TenantId == id && e.Role == EmployeeRole.Admin && e.IsActive)
                .OrderBy(e => e.CreatedAtUtc)
                .ToListAsync(ct);
            if (admins.Count == 0)
                return BadRequest(new { error = "NoAdmin" });

            admin = admins.FirstOrDefault(a => a.Id != actorId && !_superAdminIds.Contains(a.Id));
            if (admin is null)
                // Admins exist, but every one of them is an operator (or this operator). Distinct from
                // NoAdmin: the company is not adminless, it just has nobody the console may borrow, so
                // the fix is to create the customer's own admin rather than to wonder where it went.
                return BadRequest(new { error = "NoImpersonableAdmin" });
        }

        var token = _jwt.GenerateImpersonationToken(admin, actorId, ImpersonationMinutes, readOnly);

        // The CUSTOMER's own audit gets a row as well as the operator console's. Everything the borrowed
        // session then does inside the tenant is recorded under the admin's own id (AuditLog has no
        // impersonator field), and the console's log lives in SuperAdminAuditLogs behind /api/super,
        // which no tenant can read — so without this line the company has no way of knowing the platform
        // was ever in their account. TenantId is set by hand: this row belongs to the TARGET company, not
        // to whichever tenant the operator's own employee row happens to live in.
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenant.Id,
            EmployeeId = admin.Id,
            EventType = AuditEventType.ImpersonationStarted,
            Reason = $"Operator {actorId} · {admin.Role}{(readOnly ? " · baxış rejimi" : "")}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

        await AuditAsync(readOnly ? "ViewStarted" : "ImpersonationStarted", tenant.Id, tenant.Slug,
            $"{admin.FullName} ({admin.PhoneNumber}) · {admin.Role}", ct);

        return Ok(new
        {
            token,
            tenantSlug = tenant.Slug,
            tenantName = tenant.DisplayName,
            adminName = admin.FullName,
            // Which seat was borrowed, so the banner can say "manager" rather than implying admin.
            role = admin.Role.ToString(),
            expiresInMinutes = ImpersonationMinutes,
            readOnly,
        });
    }
}
