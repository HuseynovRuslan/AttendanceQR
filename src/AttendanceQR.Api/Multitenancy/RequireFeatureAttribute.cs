using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Multitenancy;

/// <summary>
/// Gate a controller or action on a per-tenant feature flag. The super-admin panel can turn a feature
/// off for one company (see <see cref="TenantFeatures"/>); this is the server-side half of that switch,
/// so a disabled feature is 403 even when someone hits the URL directly. Hiding the menu item is not a
/// gate — this is.
/// </summary>
/// <example><c>[RequireFeature(TenantFeatures.Payroll)]</c></example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireFeatureAttribute : TypeFilterAttribute
{
    public RequireFeatureAttribute(string featureKey) : base(typeof(RequireFeatureFilter))
    {
        Arguments = new object[] { featureKey };
    }
}

/// <summary>The work behind <see cref="RequireFeatureAttribute"/>. Reads the resolved tenant's opt-out
/// list and short-circuits with 403 when the feature is off. An action filter, not an authorization
/// filter, so <c>[Authorize]</c> runs first — an anonymous caller gets 401, never a 403 that would leak
/// which features a tenant has turned off.</summary>
public sealed class RequireFeatureFilter : IAsyncActionFilter
{
    private readonly string _featureKey;
    private readonly ITenantContext _tenant;
    private readonly AppDbContext _db;

    public RequireFeatureFilter(string featureKey, ITenantContext tenant, AppDbContext db)
    {
        _featureKey = featureKey;
        _tenant = tenant;
        _db = db;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // No tenant on the request → not our decision to make here. A tenant-scoped endpoint that runs
        // unresolved is already rejected upstream (fail-closed); anything else genuinely has no tenant
        // to gate, so leave it alone rather than 403 a request that was never about a feature.
        if (!_tenant.IsResolved)
        {
            await next();
            return;
        }

        // Tenants carries no query filter (it is the registry), so this reads the resolved tenant directly.
        var disabledCsv = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => t.DisabledFeatures)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (!TenantFeatures.IsEnabled(disabledCsv, _featureKey))
        {
            context.Result = new ObjectResult(new { error = "FeatureDisabled", feature = _featureKey })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
