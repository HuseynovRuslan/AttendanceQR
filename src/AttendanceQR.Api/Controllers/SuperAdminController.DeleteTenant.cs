using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
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

        var (refusal, usage) = await RefusalAsync(tenant, ct);
        // Nothing to preview for a company that cannot be deleted — and on a real customer that count
        // is 28 queries across a year of history to draw a list nobody will act on.
        var counts = refusal is null
            ? await TenantPurge.CountAsync(_db, id, ct)
            : new Dictionary<string, int>();

        return Ok(new
        {
            id = tenant.Id,
            displayName = tenant.DisplayName,
            canDelete = refusal is null,
            // Which rail stopped it, so the console can say what to do about it rather than only that
            // the button will not work.
            reason = refusal,
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

        var (refusal, usage) = await RefusalAsync(tenant, ct);
        if (refusal is not null)
            return Conflict(new { error = refusal, usage });

        // Photo keys BEFORE the rows that name them: afterwards the objects are orphans nobody can
        // find, and the bucket's retention job never touches reference/ at all. Same order and same
        // reasoning as the employee delete.
        var photoKeys = await PhotoKeysAsync(id, ct);

        var rowsDeleted = await TenantPurge.PurgeAsync(_db, id, ct);
        _db.Tenants.Remove(tenant);
        await _db.SaveChangesAsync(ct);

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

        // Photos that were named but not removed — a storage client with no endpoint configured
        // returns 0 rather than throwing (the .env-alone trap), so silence here would have read as
        // success while the faces stayed in the bucket.
        var photosPending = photoKeys.Count - photosDeleted;
        if (photosPending > 0)
        {
            _logger?.LogError(
                "Tenant {TenantId} deleted, but {Pending} of {Total} photos remain in storage",
                id, photosPending, photoKeys.Count);
        }

        // Written AFTER storage, with what actually happened. Recording photoKeys.Count beforehand
        // would have let the log say "12 şəkil" about twelve objects still sitting in the bucket.
        await AuditAsync("TenantDeleted", null, tenant.Slug,
            $"'{tenant.DisplayName}' silindi — {rowsDeleted} sətir, {photosDeleted} şəkil"
            + (photosPending > 0 ? $", {photosPending} şəkil SİLİNMƏDİ" : string.Empty), ct);

        return Ok(new { deleted = id, rowsDeleted, photosDeleted, photosPending });
    }

    /// <summary>
    /// Every reason not to delete this company, checked in the order that matters — the first rail
    /// that stops it, or null when none do.
    ///
    /// One rail is not a safeguard. The operator works alone, at speed, on a table where the rows
    /// either side of the one they meant look exactly like it, so each of these has to fail
    /// independently for the wrong company to go.
    /// </summary>
    private async Task<(string? Refusal, object Usage)> RefusalAsync(Tenant tenant, CancellationToken ct)
    {
        var id = tenant.Id;
        var records = await _db.AttendanceRecords.IgnoreQueryFilters().CountAsync(a => a.TenantId == id, ct);
        var summaries = await _db.DailySummaries.IgnoreQueryFilters().CountAsync(d => d.TenantId == id, ct);
        var visits = await _db.FieldVisits.IgnoreQueryFilters().CountAsync(v => v.TenantId == id, ct);
        var invoices = await _db.TenantInvoices.CountAsync(i => i.TenantId == id, ct);
        var usage = new { records, summaries, visits, invoices };

        // A running company is never one keystroke from deletion. Switching it off first is reversible,
        // takes a second, and puts a deliberate act between a live customer and this endpoint — which
        // matters because the row menu renders "Söndür" and "Şirkəti sil" as neighbours.
        if (tenant.IsActive)
            return ("TenantIsActive", usage);

        // One scan is somebody's day of pay.
        if (records + summaries + visits > 0)
            return ("TenantHasHistory", usage);

        // A company that has been billed is a customer whether or not anybody ever scanned — and the
        // invoice carries a TenantId, so the sweep would have taken the billing history with it,
        // silently, with nothing in the response to say so.
        if (invoices > 0)
            return ("TenantHasInvoices", usage);

        // An operator whose own employee row lives inside this company would delete their own account
        // along with it and lock themselves out of the console. SetActive already refuses the same
        // shape of mistake for suspension.
        var operators = _superAdminIds;
        if (operators.Length > 0 &&
            await _db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == id && operators.Contains(e.Id), ct))
            return ("TenantHasOperator", usage);

        // The same rail for a console team member who is not on the .env allowlist: their profile row
        // would survive the sweep and point at an employee that no longer exists, which the Team page
        // renders as a nameless entry nobody can explain.
        var profileIds = await _db.OperatorProfiles.Select(p => p.EmployeeId).ToListAsync(ct);
        if (profileIds.Count > 0 &&
            await _db.Employees.IgnoreQueryFilters().AnyAsync(e => e.TenantId == id && profileIds.Contains(e.Id), ct))
            return ("TenantHasOperator", usage);

        return (null, usage);
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
