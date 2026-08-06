using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// The operator team + their roles. WHO is an operator is the .env allowlist (adding one still needs a
// deploy — elevation is not a UI action); this only sets what each existing operator may do. Changing a
// role needs ManageTeam (Full); everything else here is a read any operator can see.
public partial class SuperAdminController
{
    // GET /api/super/team — every allowlisted operator, resolved to a person + their current role.
    [HttpGet("team")]
    public async Task<IActionResult> Team()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var ids = _superAdminIds;

        var employees = await _db.Employees.IgnoreQueryFilters()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.PhoneNumber, e.TenantId })
            .ToListAsync(ct);

        var tenantIds = employees.Select(e => e.TenantId).Distinct().ToList();
        var tenantNames = await _db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.DisplayName, ct);

        var roles = await _db.OperatorProfiles
            .Where(p => ids.Contains(p.EmployeeId))
            .ToDictionaryAsync(p => p.EmployeeId, p => p.Role, ct);

        var me = User.EmployeeId();

        var rows = ids.Select(id =>
        {
            var emp = employees.FirstOrDefault(e => e.Id == id);
            var role = roles.GetValueOrDefault(id, OperatorRoleType.Full);
            return new
            {
                employeeId = id,
                fullName = emp?.FullName ?? "(hesab tapılmadı)",
                phone = emp?.PhoneNumber,
                tenantName = emp != null ? tenantNames.GetValueOrDefault(emp.TenantId) : null,
                role = role.ToString(),
                isYou = id == me,
                resolved = emp != null,
            };
        })
        .OrderByDescending(r => r.isYou)
        .ThenBy(r => r.fullName)
        .ToList();

        return Ok(new
        {
            roles = Enum.GetNames(typeof(OperatorRoleType)),
            rows,
        });
    }

    // PUT /api/super/team/{employeeId}/role — set an operator's role.
    [HttpPut("team/{employeeId:guid}/role")]
    public async Task<IActionResult> SetOperatorRole(Guid employeeId, [FromBody] OperatorRoleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (!await CanAsync(OperatorPermission.ManageTeam, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        // A role can only be set on someone who is ALREADY an operator (in the .env allowlist). Granting a
        // role to a non-operator would imply DB-granted access, which we deliberately keep to .env.
        if (!_superAdminIds.Contains(employeeId))
            return BadRequest(new { error = "NotAnOperator" });

        // You cannot change your OWN role. On its own that only stops the obvious case — the real
        // "at least one Full always remains" guarantee is enforced under the lock below, because two
        // operators demoting each other at the same instant each slip past this check.
        if (employeeId == User.EmployeeId())
            return BadRequest(new { error = "CannotChangeOwnRole" });

        if (!Enum.TryParse<OperatorRoleType>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            return BadRequest(new { error = "RoleInvalid" });

        // Serialize every role change on one advisory lock. Without it, two Full operators demoting each
        // other concurrently would BOTH read the other as still-Full and commit, leaving ZERO Full — and
        // only Full holds ManageTeam, so team management would be unrecoverable short of a redeploy. Role
        // changes are rare, so one global lock costs nothing. It (and the guard) live inside the tx; an
        // early return disposes the tx and rolls back.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(990515)", ct);

        // Last-Full guard, read AFTER the lock so it sees any concurrent demotion that already committed.
        // Allowlisted ids with no profile row count as Full (the default), so this must fold the allowlist
        // over the profiled rows — counting OperatorProfiles alone would miss the default-Full operators.
        var profiledRoles = await _db.OperatorProfiles
            .Where(p => _superAdminIds.Contains(p.EmployeeId))
            .ToDictionaryAsync(p => p.EmployeeId, p => p.Role, ct);
        if (OperatorAccess.WouldLeaveNoFull(_superAdminIds, profiledRoles, employeeId, role))
            return BadRequest(new { error = "CannotRemoveLastFullOperator" });

        var profile = await _db.OperatorProfiles.FirstOrDefaultAsync(p => p.EmployeeId == employeeId, ct);
        if (profile is null)
        {
            profile = new OperatorProfile { EmployeeId = employeeId };
            _db.OperatorProfiles.Add(profile);
        }
        profile.Role = role;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        // The lock serializes writers, so the unique-index insert race the old try/catch caught can no
        // longer happen — one writer holds the lock while the other waits.
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var name = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == employeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? employeeId.ToString();
        await AuditAsync("OperatorRoleChanged", null, null, $"{name} → {role}", ct);
        return Ok(new { employeeId, role = role.ToString() });
    }
}
