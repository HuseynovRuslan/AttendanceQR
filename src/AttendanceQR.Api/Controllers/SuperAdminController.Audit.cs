using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// The platform audit trail. Every super-admin mutation writes one row here (see AuditAsync), and this
// is the only screen that reads them back. Kept in its own partial so the trail stays obviously
// separate from the tenant-management actions it records.
public partial class SuperAdminController
{
    // GET /api/super/audit — who did what, across every company. Most recent first; optionally narrowed
    // to a single tenant. Read-only — the log has no edit or delete path by design.
    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] int take = 100, [FromQuery] Guid? tenantId = null)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;

        // SuperAdminAuditLogs is global (no query filter), so this deliberately sees across all tenants.
        var q = _db.SuperAdminAuditLogs.AsQueryable();
        if (tenantId is Guid tid)
            q = q.Where(a => a.TargetTenantId == tid);

        var rows = await q
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct);

        return Ok(rows.Select(a => new
        {
            id = a.Id,
            actorName = a.ActorName,
            action = a.Action,
            targetTenantId = a.TargetTenantId,
            targetTenantSlug = a.TargetTenantSlug,
            details = a.Details,
            ipAddress = a.IpAddress,
            createdAtUtc = a.CreatedAtUtc,
        }));
    }

    // Records one audit row. Called right after a successful mutation so a change is never left
    // unlogged. The actor's name is resolved across tenants (IgnoreQueryFilters) because the operator's
    // employee row can live in any company. The row itself is global, so SaveChanges stamps nothing.
    private async Task AuditAsync(string action, Guid? targetTenantId, string? targetTenantSlug, string? details, CancellationToken ct)
    {
        var actorId = User.EmployeeId();
        var actorName = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.Id == actorId)
            .Select(e => e.FullName)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        _db.SuperAdminAuditLogs.Add(new SuperAdminAuditLog
        {
            ActorEmployeeId = actorId,
            ActorName = actorName,
            Action = action,
            TargetTenantId = targetTenantId,
            TargetTenantSlug = targetTenantSlug,
            Details = details,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await _db.SaveChangesAsync(ct);
    }
}
