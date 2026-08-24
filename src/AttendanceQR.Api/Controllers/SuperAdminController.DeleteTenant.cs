using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Deleting a company that never became one.
///
/// Companies get made by accident: a name typed wrong, a demo for a customer who did not sign, a test
/// of the creation form itself. Until now there was no way to remove them — only "Söndür", which hides
/// nothing and leaves the row in every list for ever. Every tenant-scoped table has an FK to Tenant
/// with Restrict, so even by hand the delete would fail on the first employee row.
///
/// So this deletes, and it deletes ONLY a company that never recorded anybody's work. One check-in is
/// somebody's pay: if a single attendance record, field visit or daily summary exists, this refuses
/// and says so, and the company can be switched off instead. There is no force flag — an override
/// exists to be used at 18:00 on a Friday, and the thing on the other side of it is a customer's
/// entire history.
///
/// What survives on purpose: the super-admin audit trail. It is keyed by TargetTenantId, not TenantId,
/// so the purge does not reach it — which is deliberate, because the row saying who deleted a company
/// and when must outlive the company.
/// </summary>
public partial class SuperAdminController
{
    // GET /api/super/tenants/{id}/deletable — what deleting this company would destroy, and whether
    // it is allowed. The console asks before it offers the button, so it never shows an action that
    // would only refuse.
    [HttpGet("tenants/{id:guid}/deletable")]
    public async Task<IActionResult> TenantDeletable(Guid id)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        var (used, usage) = await UsageAsync(id, ct);
        var counts = await TenantPurge.CountAsync(_db, id, ct);

        return Ok(new
        {
            id = tenant.Id,
            displayName = tenant.DisplayName,
            canDelete = !used,
            usage,
            // Row counts per table, so "delete" is a number the operator can recognise as their test
            // company or fail to recognise as somebody's real one.
            rows = counts,
        });
    }

    // DELETE /api/super/tenants/{id} — remove a company and everything in it, permanently.
    [HttpDelete("tenants/{id:guid}")]
    public async Task<IActionResult> DeleteTenant(Guid id, [FromBody] DeleteTenantRequest request)
    {
        if (!await CanAsync(OperatorPermission.ManageTenants, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        // The company's own name, typed. Not a checkbox and not an "are you sure": the operator works
        // alone and the two rows either side of the one they meant look exactly like it.
        if (!string.Equals(request.Confirm?.Trim(), tenant.DisplayName, StringComparison.Ordinal))
            return BadRequest(new { error = "ConfirmMismatch", expected = tenant.DisplayName });

        var (used, usage) = await UsageAsync(id, ct);
        if (used)
            return Conflict(new { error = "TenantHasHistory", usage });

        // Photo keys BEFORE the rows that name them: afterwards the objects are orphans nobody can
        // find, and the bucket's retention job never touches reference/ at all. Same order and same
        // reasoning as the employee delete.
        var photoKeys = await PhotoKeysAsync(id, ct);

        var rowsDeleted = await TenantPurge.PurgeAsync(_db, id, ct);
        _db.Tenants.Remove(tenant);
        await _db.SaveChangesAsync(ct);

        // Written after the delete, from the operator's own scope, into the one log the purge cannot
        // reach. Details rather than an id, because the id now refers to nothing.
        await AuditAsync("TenantDeleted", null, tenant.Slug,
            $"'{tenant.DisplayName}' silindi — {rowsDeleted} sətir, {photoKeys.Count} şəkil", ct);

        // Object storage has no transaction to join, so it goes last and best-effort: the rows are
        // already gone, and a failure here leaves objects nobody references rather than a company
        // half-deleted.
        var photosDeleted = 0;
        if (photoKeys.Count > 0 && _photoStorage is not null)
        {
            try
            {
                photosDeleted = await _photoStorage.DeleteObjectsAsync(photoKeys);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex,
                    "Tenant {TenantId} deleted, but {Count} of its photos could not be removed from storage",
                    id, photoKeys.Count);
            }
        }

        return Ok(new { deleted = id, rowsDeleted, photosDeleted });
    }

    /// <summary>
    /// Whether anybody ever worked here. One record of somebody's attendance is the line between a
    /// test company and a customer's history, and it is the only thing this endpoint refuses on.
    /// </summary>
    private async Task<(bool Used, object Usage)> UsageAsync(Guid tenantId, CancellationToken ct)
    {
        var records = await _db.AttendanceRecords.IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId, ct);
        var summaries = await _db.DailySummaries.IgnoreQueryFilters().CountAsync(d => d.TenantId == tenantId, ct);
        var visits = await _db.FieldVisits.IgnoreQueryFilters().CountAsync(v => v.TenantId == tenantId, ct);
        return (records + summaries + visits > 0, new { records, summaries, visits });
    }

    /// <summary>Every photo this company put in the bucket: enrolment selfies, check-ins, field visits.</summary>
    private async Task<List<string>> PhotoKeysAsync(Guid tenantId, CancellationToken ct)
    {
        var keys = new List<string>();
        keys.AddRange(await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.ReferencePhotoKey != null)
            .Select(e => e.ReferencePhotoKey!)
            .ToListAsync(ct));
        keys.AddRange(await _db.AttendanceRecords.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.CheckInPhotoKey != null)
            .Select(a => a.CheckInPhotoKey!)
            .ToListAsync(ct));
        // Three nullable columns on one row: pulled back and flattened here rather than with a
        // SelectMany over an array literal, which does not translate to SQL.
        var visits = await _db.FieldVisits.IgnoreQueryFilters()
            .Where(v => v.TenantId == tenantId)
            .Select(v => new { v.CheckInPhotoKey, v.CheckOutPhotoKey, v.WorkPhotoKey })
            .ToListAsync(ct);
        keys.AddRange(visits
            .SelectMany(v => new[] { v.CheckInPhotoKey, v.CheckOutPhotoKey, v.WorkPhotoKey })
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!));
        return keys.Distinct().ToList();
    }
}
