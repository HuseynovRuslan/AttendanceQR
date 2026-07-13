using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>Public tenant info the app needs to brand itself before login. The tenant is resolved
/// from the request Origin (subdomain) by the middleware, so no auth is required.</summary>
[ApiController]
[Route("api/tenant")]
public class TenantController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public TenantController(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    // GET /api/tenant/branding — the current tenant's display name + accent colour.
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<IActionResult> Branding()
    {
        // Tenants is not query-filtered (it's the tenant registry itself), so this reads the resolved
        // tenant directly.
        var b = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => new { displayName = t.DisplayName, color = t.Color })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        return Ok(b ?? new { displayName = string.Empty, color = (string?)null });
    }
}
