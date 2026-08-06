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

        // You cannot change your OWN role — this is what guarantees at least one Full operator always
        // remains (whoever is making the change), so nobody can lock the team out of team management.
        if (employeeId == User.EmployeeId())
            return BadRequest(new { error = "CannotChangeOwnRole" });

        if (!Enum.TryParse<OperatorRoleType>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
            return BadRequest(new { error = "RoleInvalid" });

        var profile = await _db.OperatorProfiles.FirstOrDefaultAsync(p => p.EmployeeId == employeeId, ct);
        if (profile is null)
        {
            profile = new OperatorProfile { EmployeeId = employeeId };
            _db.OperatorProfiles.Add(profile);
        }
        profile.Role = role;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race to create the row — re-read and re-apply idempotently (unique index on EmployeeId).
            _db.ChangeTracker.Clear();
            profile = await _db.OperatorProfiles.FirstAsync(p => p.EmployeeId == employeeId, ct);
            profile.Role = role;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        var name = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == employeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? employeeId.ToString();
        await AuditAsync("OperatorRoleChanged", null, null, $"{name} → {role}", ct);
        return Ok(new { employeeId, role = role.ToString() });
    }
}
