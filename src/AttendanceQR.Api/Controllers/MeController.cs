using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// What the signed-in person may do, according to the permission model — read from the database on every
/// call, so a role given or taken shows on the next one with nobody signed out. The panel draws its menu
/// from this (docs/strukturlar-ve-selahiyyetler.md §5); the server still decides every request on its own.
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
        var legacyRole = User.Role();

        // Names, so the panel can say «Aeroport yolu» and «Filial: Mərdəkan» rather than print ids. Only the
        // areas this person's own assignments reach — a timekeeper is not handed the company's map.
        var roleIds = access.Grants.Select(g => g.RoleId).Distinct().ToList();
        var roleNames = roleIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.AccessRoles.AsNoTracking().Where(r => roleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var structureIds = access.Grants.Where(g => g.StructureId != null).Select(g => g.StructureId!.Value).Distinct().ToList();
        var structureNames = structureIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Structures.AsNoTracking().Where(s => structureIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var everywhere = access.Grants.Any(g => g.Scope == AccessScopeType.Company);
        var reached = access.Grants.SelectMany(g => g.LocationIds).Distinct().ToList();
        var branches = everywhere || reached.Count > 0
            ? await _db.Locations.AsNoTracking()
                .Where(l => l.IsActive && (everywhere || reached.Contains(l.Id)))
                .OrderBy(l => l.Name)
                .Select(l => new { id = l.Id, name = l.Name, structureId = l.StructureId })
                .ToListAsync(ct)
            : [];

        return Ok(new
        {
            mode = mode.ToString(),
            // The role today's gates read from the token. Until Phase 5 it, not the model, opens the pages —
            // the panel shows what the model would open beside it, marked as coming after the switch.
            legacyRole = legacyRole.ToString(),
            // Whether the panel is this person's at all: today's two panel roles, or any role in the new model
            // (a timekeeper who is plain staff today sees their area and what will open for them). Once the
            // company is switched to Enforced, only the model: the token's role opens nothing any more.
            panel = mode == AuthzMode.Enforced
                ? access.Grants.Count > 0
                : legacyRole is EmployeeRole.Admin or EmployeeRole.Manager || access.Grants.Count > 0,
            // The Company administrator role, company-wide — who gets the admin's shape of a screen in Enforced
            // (AccessProjection). Everybody else with a right gets the narrowed shape, over where they hold it.
            companyAdmin = AccessProjection.IsCompanyAdmin(access),
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
                roleName = roleNames.GetValueOrDefault(g.RoleId),
                template = g.TemplateKey,
                scope = g.Scope.ToString(),
                structureId = g.StructureId,
                structureName = g.StructureId is Guid s ? structureNames.GetValueOrDefault(s) : null,
                locationIds = g.LocationIds.Order().ToArray(),
            }),
            branches,
        });
    }
}
