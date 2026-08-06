using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

// Commercial plan, soft limits and per-tenant feature flags. Setting them is a super-admin action, so
// it is audited; reading the feature catalogue is not.
public partial class SuperAdminController
{
    private static readonly HashSet<string> KnownPlans =
        new(StringComparer.OrdinalIgnoreCase) { "Start", "Biznes", "Korporativ", "Enterprise" };

    // GET /api/super/features — the togglable feature catalogue (key + Azerbaijani label) so the panel
    // renders the right switches without hard-coding a list that could drift from the backend.
    [HttpGet("features")]
    public IActionResult Features()
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });
        return Ok(TenantFeatures.All.Select(f => new { key = f.Key, label = f.Label }));
    }

    // PUT /api/super/tenants/{id}/plan — set a company's plan, soft limits and disabled features in one
    // call. A null/blank plan clears it; a null or non-positive limit means unlimited; DisabledFeatures
    // is the opt-out list (unknown keys are dropped).
    [HttpPut("tenants/{id:guid}/plan")]
    public async Task<IActionResult> SetPlan(Guid id, [FromBody] TenantPlanRequest request)
    {
        if (!IsSuperAdmin)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotSuperAdmin" });

        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        var plan = string.IsNullOrWhiteSpace(request.Plan) ? null : request.Plan.Trim();
        if (plan is not null && !KnownPlans.Contains(plan))
            return BadRequest(new { error = "PlanInvalid" });

        tenant.Plan = plan;
        tenant.MaxEmployees = request.MaxEmployees is int me and > 0 ? me : null;
        tenant.MaxLocations = request.MaxLocations is int ml and > 0 ? ml : null;
        // >= 0 sets it (0 = a comped/free tenant); null or negative clears → billing uses the formula.
        tenant.MonthlyPriceOverride = request.MonthlyPriceOverride is decimal p and >= 0m ? p : null;
        tenant.DisabledFeatures = TenantFeatures.ToCsv(request.DisabledFeatures ?? Array.Empty<string>());
        await _db.SaveChangesAsync(ct);

        await AuditAsync("TenantPlanChanged", tenant.Id, tenant.Slug,
            $"plan={plan ?? "—"}, maxİşçi={tenant.MaxEmployees?.ToString() ?? "∞"}, " +
            $"maxFilial={tenant.MaxLocations?.ToString() ?? "∞"}, " +
            $"qiymət={(tenant.MonthlyPriceOverride is decimal pr ? $"{pr:0.##}₼" : "formul")}, " +
            $"bağlı=[{tenant.DisabledFeatures ?? ""}]", ct);

        return Ok(new
        {
            id = tenant.Id,
            plan = tenant.Plan,
            maxEmployees = tenant.MaxEmployees,
            maxLocations = tenant.MaxLocations,
            monthlyPriceOverride = tenant.MonthlyPriceOverride,
            disabledFeatures = TenantFeatures.ParseDisabled(tenant.DisabledFeatures),
        });
    }
}
