using AttendanceQR.Domain.Access;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// What the signed-in person may do, according to the permission model — read from the database on every
/// call, so a role given or taken shows on the next one with nobody signed out. The panel will draw its menu
/// and buttons from this instead of the token's role (docs/strukturlar-ve-selahiyyetler.md §5); the server
/// still decides every request on its own.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me")]
public class MeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public MeController(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // GET /api/me/access
    [HttpGet("access")]
    public async Task<IActionResult> Access()
    {
        var ct = HttpContext.RequestAborted;
        var access = (await AccessGraph.LoadAsync(_db, ct)).For(User.EmployeeId());
        var mode = await AuthzObserver.ModeAsync(_db, _cache, ct);

        return Ok(new
        {
            mode = mode.ToString(),
            permissions = access.Permissions.Order().Select(key => new
            {
                key,
                label = AccessPermissions.Labels[key],
                company = access.HasAtCompany(key),
                locationIds = access.HasAtCompany(key) ? [] : access.BranchesFor(key)!.Order().ToArray(),
            }),
            assignments = access.Grants.Select(g => new
            {
                roleId = g.RoleId,
                template = g.TemplateKey,
                scope = g.Scope.ToString(),
                structureId = g.StructureId,
                locationIds = g.LocationIds.Order().ToArray(),
            }),
        });
    }
}
