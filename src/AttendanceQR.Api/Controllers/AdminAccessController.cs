using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Roles, and who holds them where — Phase 3 of docs/strukturlar-ve-selahiyyetler.md.
///
/// Every decision here is the permission model's own (AccessRules.CanGrant / CanRevoke), read from the
/// database on every request: a role given or taken away counts from the next request on, and nobody is
/// signed out (the owner's rule — no forced logout). The gate is still today's «Admin» while the rest of the
/// panel is; somebody holding the right to assign below company level gets their screen in Phase 4.
///
/// A support session only looks. Handing out or taking away power changes who can do what in somebody
/// else's company, and that is never support's to do — the same line AdminController draws at promotions.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/access")]
public class AdminAccessController : ControllerBase
{
    private const int MaxBranchesPerCall = 100;
    private const int NameMax = 120;
    private const int DescriptionMax = 500;

    private readonly AppDbContext _db;
    private readonly OperatorAllowlist _operators;

    public AdminAccessController(AppDbContext db, OperatorAllowlist operators)
    {
        _db = db;
        _operators = operators;
    }

    /// <summary>The permission keys with their Azerbaijani names, and the six template roles — what the role
    /// editor draws its checkboxes from.</summary>
    [Permission(AccessPermissions.AssignmentsView)]
    [HttpGet("catalogue")]
    public IActionResult Catalogue() => Ok(new
    {
        permissions = AccessPermissions.Labels.Select(kv => new
        {
            key = kv.Key,
            label = kv.Value,
            companyOnly = AccessPermissions.CompanyOnly.Contains(kv.Key),
            meta = AccessPermissions.Meta.Contains(kv.Key),
        }),
        templates = AccessRoleTemplates.All.Select(t => new
        {
            key = t.Key, name = t.Name, description = t.Description, isLocked = t.IsLocked, isLegacy = t.IsLegacy,
        }),
    });

    [Permission(AccessPermissions.AssignmentsView)]
    [HttpGet("roles")]
    public async Task<IActionResult> Roles([FromQuery] bool includeArchived = false)
    {
        var ct = HttpContext.RequestAborted;
        var counts = await _db.RoleAssignments.AsNoTracking()
            .Where(a => a.RevokedAtUtc == null)
            .GroupBy(a => a.AccessRoleId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var roles = await _db.AccessRoles.AsNoTracking()
            .Where(r => includeArchived || r.ArchivedAtUtc == null)
            .ToListAsync(ct);

        return Ok(roles
            // Ordinal on purpose: a culture lookup can throw in an invariant-globalization container, and a
            // handful of role names needs no collation.
            .OrderBy(RoleOrder).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => ProjectRole(r, counts.GetValueOrDefault(r.Id))));
    }

    /// <summary>A company's own role. Only power the creator holds over the whole company can go into it.</summary>
    [Permission(AccessPermissions.RolesManage, AtCompany = true)]
    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] AccessRoleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (User.IsImpersonating())
            return NotDuringSupport();
        var me = await MeAsync(ct);
        if (!me.Access.HasAtCompany(AccessPermissions.RolesManage))
            return Refused("PermissionNotHeld", AccessPermissions.RolesManage);
        if (ValidateRole(request, me.Access) is { } invalid)
            return invalid;

        var name = request.Name!.Trim();
        if (await _db.AccessRoles.AnyAsync(r => r.ArchivedAtUtc == null && r.Name == name, ct))
            return Conflict(new { error = "NameTaken" });

        var role = new AccessRole
        {
            TenantId = _db.CurrentTenantId,
            Name = name,
            Description = Clean(request.Description, DescriptionMax),
            Permissions = request.Permissions!.Distinct().Order().ToList(),
        };
        _db.AccessRoles.Add(role);
        Audit(AuditEventType.AccessRoleChanged, me.Id, $"By {me.Id}: role «{role.Name}» created — {role.Permissions.Count} permission(s)");
        if (await SaveAsync(ct) is { } conflict)
            return conflict;
        return Ok(ProjectRole(role, 0));
    }

    /// <summary>
    /// Renames a role or changes what it allows — for everybody who holds it, from their next request on. The
    /// three locked roles never change here: the company administrator is the way back in, and the two
    /// legacy roles ARE today's access, which only the code that reads Employee.Role may say.
    /// </summary>
    [Permission(AccessPermissions.RolesManage, AtCompany = true)]
    [HttpPut("roles/{id:guid}")]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] AccessRoleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (User.IsImpersonating())
            return NotDuringSupport();
        var me = await MeAsync(ct);
        if (!me.Access.HasAtCompany(AccessPermissions.RolesManage))
            return Refused("PermissionNotHeld", AccessPermissions.RolesManage);

        var role = await _db.AccessRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return NotFound(new { error = "RoleNotFound" });
        if (role.IsLocked)
            return Conflict(new { error = "RoleLocked" });
        if (role.ArchivedAtUtc is not null)
            return Conflict(new { error = "RoleArchived" });
        if (ValidateRole(request, me.Access) is { } invalid)
            return invalid;

        var name = request.Name!.Trim();
        if (await _db.AccessRoles.AnyAsync(r => r.Id != id && r.ArchivedAtUtc == null && r.Name == name, ct))
            return Conflict(new { error = "NameTaken" });

        var permissions = request.Permissions!.Distinct().Order().ToList();
        // A company-only right cannot ride on a role somebody already holds over a structure or a branch: it
        // would mean, say, «holidays» for one branch — exactly what CanGrant refuses at the door.
        var companyOnly = permissions.Where(AccessPermissions.CompanyOnly.Contains).ToList();
        if (companyOnly.Count > 0 && await _db.RoleAssignments.AnyAsync(
                a => a.AccessRoleId == id && a.RevokedAtUtc == null && a.Scope != AccessScopeType.Company, ct))
            return Conflict(new { error = "CompanyOnlyPermissionInAreaAssignment", permission = companyOnly[0] });

        var added = permissions.Except(role.Permissions).ToList();
        var removed = role.Permissions.Except(permissions).ToList();
        var renamed = role.Name != name ? $" «{role.Name}» → «{name}»;" : "";
        role.Name = name;
        role.Description = Clean(request.Description, DescriptionMax);
        role.Permissions = permissions;
        Audit(AuditEventType.AccessRoleChanged, me.Id,
            $"By {me.Id}: role «{name}» edited;{renamed} +[{string.Join(",", added)}] -[{string.Join(",", removed)}]");
        if (await SaveAsync(ct) is { } conflict)
            return conflict;
        return Ok(ProjectRole(role, await ActiveCountAsync(id, ct)));
    }

    /// <summary>Puts a role away. Only an unused one — taking power from the people who hold it is done one
    /// assignment at a time, each on the record, not as a side effect of tidying the role list.</summary>
    [Permission(AccessPermissions.RolesManage, AtCompany = true)]
    [HttpPost("roles/{id:guid}/archive")]
    public async Task<IActionResult> ArchiveRole(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        if (User.IsImpersonating())
            return NotDuringSupport();
        var me = await MeAsync(ct);
        if (!me.Access.HasAtCompany(AccessPermissions.RolesManage))
            return Refused("PermissionNotHeld", AccessPermissions.RolesManage);

        var role = await _db.AccessRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return NotFound(new { error = "RoleNotFound" });
        if (role.IsLocked)
            return Conflict(new { error = "RoleLocked" });
        if (role.ArchivedAtUtc is not null)
            return Ok(ProjectRole(role, 0));
        var inUse = await ActiveCountAsync(id, ct);
        if (inUse > 0)
            return Conflict(new { error = "RoleInUse", count = inUse });

        role.ArchivedAtUtc = DateTime.UtcNow;
        Audit(AuditEventType.AccessRoleChanged, me.Id, $"By {me.Id}: role «{role.Name}» archived");
        await _db.SaveChangesAsync(ct);
        return Ok(ProjectRole(role, 0));
    }

    [Permission(AccessPermissions.RolesManage, AtCompany = true)]
    [HttpPost("roles/{id:guid}/restore")]
    public async Task<IActionResult> RestoreRole(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        if (User.IsImpersonating())
            return NotDuringSupport();
        var me = await MeAsync(ct);
        if (!me.Access.HasAtCompany(AccessPermissions.RolesManage))
            return Refused("PermissionNotHeld", AccessPermissions.RolesManage);

        var role = await _db.AccessRoles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null)
            return NotFound(new { error = "RoleNotFound" });
        if (role.ArchivedAtUtc is null)
            return Ok(ProjectRole(role, await ActiveCountAsync(id, ct)));
        if (await _db.AccessRoles.AnyAsync(r => r.Id != id && r.ArchivedAtUtc == null && r.Name == role.Name, ct))
            return Conflict(new { error = "NameTaken" });

        role.ArchivedAtUtc = null;
        Audit(AuditEventType.AccessRoleChanged, me.Id, $"By {me.Id}: role «{role.Name}» restored");
        if (await SaveAsync(ct) is { } conflict)
            return conflict;
        return Ok(ProjectRole(role, 0));
    }

    /// <summary>
    /// Who holds what, where — the ones the caller may see: everything with company-wide sight, otherwise the
    /// assignments over the areas their «assignments.view» reaches. Each row says whether THIS caller may take
    /// it away, by the same rule the revoke applies, so the screen offers only what will work.
    /// </summary>
    [Permission(AccessPermissions.AssignmentsView)]
    [HttpGet("assignments")]
    public async Task<IActionResult> Assignments(
        [FromQuery] Guid? employeeId = null, [FromQuery] Guid? roleId = null, [FromQuery] bool includeRevoked = false)
    {
        var ct = HttpContext.RequestAborted;
        var me = await MeAsync(ct);
        var rows = await _db.RoleAssignments.AsNoTracking()
            .Where(a => (includeRevoked || a.RevokedAtUtc == null)
                        && (employeeId == null || a.EmployeeId == employeeId)
                        && (roleId == null || a.AccessRoleId == roleId))
            .OrderBy(a => a.RevokedAtUtc != null).ThenByDescending(a => a.CreatedAtUtc)
            .Take(2000)
            .ToListAsync(ct);
        var names = await NamesAsync(rows, ct);
        return Ok(rows.Where(a => CanSeeArea(me.Access, a.Scope, a.StructureId, a.LocationId))
            .Select(a => ProjectAssignment(a, names, me)));
    }

    /// <summary>One person: what they hold, and what that adds up to — for the «Hüquqlar» card on a profile.</summary>
    [Permission(AccessPermissions.AssignmentsView)]
    [HttpGet("people/{employeeId:guid}")]
    public async Task<IActionResult> Person(Guid employeeId)
    {
        var ct = HttpContext.RequestAborted;
        var person = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.FullName, e.LocationId, e.Role, e.IsActive })
            .FirstOrDefaultAsync(ct);
        if (person is null)
            return NotFound(new { error = "EmployeeNotFound" });
        var me = await MeAsync(ct);
        if (!me.Access.Covers(AccessPermissions.AssignmentsView, person.LocationId) && person.Id != me.Id)
            return Refused("OutOfScope");

        var rows = await _db.RoleAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.RevokedAtUtc == null)
            .ToListAsync(ct);
        var names = await NamesAsync(rows, ct);
        var access = me.Graph.For(employeeId);
        var branchNames = names.Locations;

        return Ok(new
        {
            employeeId = person.Id,
            fullName = person.FullName,
            isOperator = _operators.Ids.Contains(person.Id),
            assignments = rows.Select(a => ProjectAssignment(a, names, me)),
            permissions = access.Permissions.Where(AccessPermissions.Labels.ContainsKey).Order().Select(key => new
            {
                key,
                label = AccessPermissions.Labels[key],
                company = access.HasAtCompany(key),
                branches = access.BranchesFor(key) is { } set
                    ? set.Select(id => branchNames.GetValueOrDefault(id, "?")).Order().ToList()
                    : [],
            }),
        });
    }

    /// <summary>
    /// What the caller may give THIS person, and where — the simple assignment dialog shows only these, so
    /// a daily «make Aynur the tabelçi of two branches» needs no permissions expert. The server re-checks
    /// everything when the assignment is saved; this only spares the clicks that would be refused.
    /// </summary>
    [Permission(AccessPermissions.AssignmentsManage)]
    [HttpGet("assignable")]
    public async Task<IActionResult> Assignable([FromQuery] Guid employeeId)
    {
        var ct = HttpContext.RequestAborted;
        var person = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.LocationId, e.IsActive })
            .FirstOrDefaultAsync(ct);
        if (person is null)
            return NotFound(new { error = "EmployeeNotFound" });
        var me = await MeAsync(ct);
        var assignee = me.Graph.For(person.Id);
        var isOperator = _operators.Ids.Contains(person.Id);

        var roles = await _db.AccessRoles.AsNoTracking().Where(r => r.ArchivedAtUtc == null).ToListAsync(ct);
        var structures = await _db.Structures.AsNoTracking().Where(s => s.ArchivedAtUtc == null)
            .Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var branches = await _db.Locations.AsNoTracking().Where(l => l.IsActive)
            .Select(l => new { l.Id, l.Name, l.StructureId }).ToListAsync(ct);

        GrantDecision Try(AccessRole r, AccessScopeType scope, Guid? s, Guid? l) =>
            AccessRules.CanGrant(me.Access, r.Permissions, scope, s, l, person.Id, isOperator, person.LocationId, assignee, me.Home);

        var options = roles
            .Where(r => !IsLegacy(r))
            .OrderBy(RoleOrder).ThenBy(r => r.Name)
            .Select(r => new
            {
                roleId = r.Id,
                name = r.Name,
                templateKey = r.TemplateKey,
                company = Try(r, AccessScopeType.Company, null, null).Allowed,
                structureIds = structures.Where(s => Try(r, AccessScopeType.Structure, s.Id, null).Allowed).Select(s => s.Id).ToList(),
                locationIds = branches.Where(b => Try(r, AccessScopeType.Branch, null, b.Id).Allowed).Select(b => b.Id).ToList(),
            })
            .Where(o => o.company || o.structureIds.Count > 0 || o.locationIds.Count > 0)
            .ToList();

        return Ok(new
        {
            employeeActive = person.IsActive,
            // Why nothing can be offered, when nothing can — the dialog says it instead of showing an empty list.
            reason = options.Count > 0 ? null
                : isOperator ? "CannotManageOperator"
                : person.Id == me.Id ? "SelfGrant"
                : "NothingAssignable",
            roles = options,
            structures = structures.OrderBy(s => s.Name),
            branches = branches.OrderBy(b => b.Name),
        });
    }

    /// <summary>
    /// Gives a role. Each area is checked on its own (CanGrant) before anything is written; one refusal refuses
    /// the call, so a half-applied «three branches» never happens. An area the person already holds the role
    /// over is counted, not doubled. Effective from the person's next request.
    /// </summary>
    [Permission(AccessPermissions.AssignmentsManage)]
    [HttpPost("assignments")]
    public async Task<IActionResult> Assign([FromBody] AssignRoleRequest request)
    {
        var ct = HttpContext.RequestAborted;
        if (User.IsImpersonating())
            return NotDuringSupport();

        var person = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (person is null)
            return NotFound(new { error = "EmployeeNotFound" });
        if (!person.IsActive)
            return Conflict(new { error = "EmployeeInactive" });
        var role = await _db.AccessRoles.FirstOrDefaultAsync(r => r.Id == request.RoleId && r.ArchivedAtUtc == null, ct);
        if (role is null)
            return NotFound(new { error = "RoleNotFound" });
        // Today's two roles follow today's data — the employee form's role and branches — and nothing else.
        if (IsLegacy(role))
            return Conflict(new { error = "LegacyRole" });

        var areas = new List<(Guid? Structure, Guid? Location)>();
        switch (request.Scope)
        {
            case AccessScopeType.Company:
                if (request.StructureId is not null || request.LocationIds is { Count: > 0 })
                    return BadRequest(new { error = "ScopeMismatch" });
                areas.Add((null, null));
                break;
            case AccessScopeType.Structure:
                if (request.StructureId is not Guid sid || request.LocationIds is { Count: > 0 })
                    return BadRequest(new { error = "ScopeMismatch" });
                if (!await _db.Structures.AnyAsync(s => s.Id == sid && s.ArchivedAtUtc == null, ct))
                    return NotFound(new { error = "StructureNotFound" });
                areas.Add((sid, null));
                break;
            case AccessScopeType.Branch:
                var ids = (request.LocationIds ?? []).Distinct().ToList();
                if (request.StructureId is not null || ids.Count == 0)
                    return BadRequest(new { error = "ScopeMismatch" });
                if (ids.Count > MaxBranchesPerCall)
                    return BadRequest(new { error = "TooMany" });
                var found = await _db.Locations.Where(l => ids.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
                if (found.Count != ids.Count)
                    return NotFound(new { error = "LocationNotFound" });
                areas.AddRange(ids.Select(l => ((Guid?)null, (Guid?)l)));
                break;
            default:
                return BadRequest(new { error = "ScopeMismatch" });
        }

        var me = await MeAsync(ct);
        var assignee = me.Graph.For(person.Id);
        var isOperator = _operators.Ids.Contains(person.Id);
        foreach (var (structureId, locationId) in areas)
        {
            var decision = AccessRules.CanGrant(me.Access, role.Permissions, request.Scope, structureId, locationId,
                person.Id, isOperator, person.LocationId, assignee, me.Home);
            if (!decision.Allowed)
                return Refused(decision.Error!, decision.Permission);
        }

        // Already held over an area — by anybody's hand, the system's included — is counted, not doubled: a
        // second active row for one key is what the unique index refuses.
        var held = await _db.RoleAssignments
            .Where(a => a.EmployeeId == person.Id && a.AccessRoleId == role.Id && a.Scope == request.Scope && a.RevokedAtUtc == null)
            .Select(a => new { a.StructureId, a.LocationId })
            .ToListAsync(ct);
        var created = new List<RoleAssignment>();
        foreach (var (structureId, locationId) in areas)
        {
            if (held.Any(h => h.StructureId == structureId && h.LocationId == locationId))
                continue;
            var row = new RoleAssignment
            {
                TenantId = _db.CurrentTenantId, EmployeeId = person.Id, AccessRoleId = role.Id, Scope = request.Scope,
                StructureId = structureId, LocationId = locationId, CreatedByEmployeeId = me.Id,
            };
            _db.RoleAssignments.Add(row);
            created.Add(row);
        }
        if (created.Count == 0)
            return Ok(new { created = Array.Empty<object>(), alreadyHeld = areas.Count });

        var names = await NamesAsync(created, ct, extraEmployees: [me.Id]);
        foreach (var row in created)
            Audit(AuditEventType.AccessGranted, person.Id,
                $"By {me.Id}: «{role.Name}» @ {AreaName(row.Scope, row.StructureId, row.LocationId, names)}");
        if (await SaveAsync(ct) is { } conflict)
            return conflict;

        me = await MeAsync(ct); // the caller's view of the rows now includes them
        return Ok(new
        {
            created = created.Select(a => ProjectAssignment(a, names, me)),
            alreadyHeld = areas.Count - created.Count,
        });
    }

    /// <summary>Takes one assignment away — the row stays, with who took it and when. Effective from the
    /// person's next request.</summary>
    [Permission(AccessPermissions.AssignmentsManage)]
    [HttpPost("assignments/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        if (User.IsImpersonating())
            return NotDuringSupport();

        var row = await _db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (row is null)
            return NotFound(new { error = "AssignmentNotFound" });
        if (row.RevokedAtUtc is not null)
            return Conflict(new { error = "AlreadyRevoked" });
        var role = await _db.AccessRoles.AsNoTracking().FirstAsync(r => r.Id == row.AccessRoleId, ct);
        var person = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == row.EmployeeId).Select(e => new { e.Id, e.LocationId }).FirstAsync(ct);

        var me = await MeAsync(ct);
        var decision = AccessRules.CanRevoke(me.Access, row.CreatedByEmployeeId is null, role.Permissions, row.Scope,
            row.StructureId, row.LocationId, person.Id, _operators.Ids.Contains(person.Id), person.LocationId,
            me.Graph.For(person.Id), me.Home);
        if (!decision.Allowed)
            return Refused(decision.Error!, decision.Permission);

        row.RevokedAtUtc = DateTime.UtcNow;
        row.RevokedByEmployeeId = me.Id;
        var names = await NamesAsync([row], ct);
        Audit(AuditEventType.AccessRevoked, person.Id,
            $"By {me.Id}: «{role.Name}» @ {AreaName(row.Scope, row.StructureId, row.LocationId, names)} taken away");
        await _db.SaveChangesAsync(ct);
        return Ok(ProjectAssignment(row, await NamesAsync([row], ct, extraEmployees: [me.Id]), me));
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private sealed record Me(Guid Id, Guid? Home, AccessGraph Graph, EffectiveAccess Access);

    private sealed record Names(
        Dictionary<Guid, (string Name, Guid Home)> Employees, Dictionary<Guid, AccessRole> Roles,
        Dictionary<Guid, string> Structures, Dictionary<Guid, string> Locations);

    /// <summary>The caller, as the permission model sees them on THIS request.</summary>
    private async Task<Me> MeAsync(CancellationToken ct)
    {
        var id = User.EmployeeId();
        var graph = await AccessGraph.LoadAsync(_db, ct);
        var home = await _db.Employees.AsNoTracking().Where(e => e.Id == id).Select(e => (Guid?)e.LocationId).FirstOrDefaultAsync(ct);
        return new Me(id, home, graph, graph.For(id));
    }

    private async Task<Names> NamesAsync(IReadOnlyCollection<RoleAssignment> rows, CancellationToken ct, Guid[]? extraEmployees = null)
    {
        var people = rows.Select(a => a.EmployeeId)
            .Concat(rows.Where(a => a.CreatedByEmployeeId != null).Select(a => a.CreatedByEmployeeId!.Value))
            .Concat(rows.Where(a => a.RevokedByEmployeeId != null).Select(a => a.RevokedByEmployeeId!.Value))
            .Concat(extraEmployees ?? [])
            .Distinct().ToList();
        var employees = await _db.Employees.AsNoTracking().Where(e => people.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.LocationId }).ToDictionaryAsync(e => e.Id, e => (e.FullName, e.LocationId), ct);
        var roleIds = rows.Select(a => a.AccessRoleId).Distinct().ToList();
        var roles = await _db.AccessRoles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var structures = await _db.Structures.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var locations = await _db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        return new Names(employees, roles, structures, locations);
    }

    private object ProjectAssignment(RoleAssignment a, Names names, Me me)
    {
        var role = names.Roles.GetValueOrDefault(a.AccessRoleId);
        var person = names.Employees.GetValueOrDefault(a.EmployeeId);
        var canRevoke = a.RevokedAtUtc is null && role is not null && AccessRules.CanRevoke(
            me.Access, a.CreatedByEmployeeId is null, role.Permissions, a.Scope, a.StructureId, a.LocationId,
            a.EmployeeId, _operators.Ids.Contains(a.EmployeeId), person.Home, me.Graph.For(a.EmployeeId), me.Home).Allowed;
        return new
        {
            id = a.Id,
            employeeId = a.EmployeeId,
            employeeName = person.Name,
            employeeHome = names.Locations.GetValueOrDefault(person.Home),
            roleId = a.AccessRoleId,
            roleName = role?.Name,
            templateKey = role?.TemplateKey,
            scope = a.Scope,
            structureId = a.StructureId,
            locationId = a.LocationId,
            area = AreaName(a.Scope, a.StructureId, a.LocationId, names),
            system = a.CreatedByEmployeeId is null,
            createdAtUtc = a.CreatedAtUtc,
            createdByName = a.CreatedByEmployeeId is Guid by ? names.Employees.GetValueOrDefault(by).Name : null,
            revokedAtUtc = a.RevokedAtUtc,
            revokedByName = a.RevokedByEmployeeId is Guid rb ? names.Employees.GetValueOrDefault(rb).Name : null,
            canRevoke,
        };
    }

    private static object ProjectRole(AccessRole r, int activeAssignments) => new
    {
        id = r.Id,
        name = r.Name,
        description = r.Description,
        templateKey = r.TemplateKey,
        isLocked = r.IsLocked,
        isLegacy = IsLegacy(r),
        permissions = r.Permissions.Where(AccessPermissions.All.Contains).Order().ToList(),
        activeAssignments,
        archived = r.ArchivedAtUtc is not null,
    };

    private static string AreaName(AccessScopeType scope, Guid? structureId, Guid? locationId, Names names) => scope switch
    {
        AccessScopeType.Company => "Bütün şirkət",
        AccessScopeType.Structure => $"Struktur: {names.Structures.GetValueOrDefault(structureId ?? Guid.Empty, "?")}",
        _ => $"Filial: {names.Locations.GetValueOrDefault(locationId ?? Guid.Empty, "?")}",
    };

    /// <summary>May the caller see an assignment over this area? Company-wide sight sees all; otherwise the area
    /// must be inside where their «assignments.view» reaches.</summary>
    private static bool CanSeeArea(EffectiveAccess me, AccessScopeType scope, Guid? structureId, Guid? locationId) =>
        me.HasAtCompany(AccessPermissions.AssignmentsView)
        || scope switch
        {
            AccessScopeType.Structure => structureId is Guid s && me.CoversStructure(AccessPermissions.AssignmentsView, s),
            AccessScopeType.Branch => locationId is Guid l && me.Covers(AccessPermissions.AssignmentsView, l),
            _ => false,
        };

    private static bool IsLegacy(AccessRole r) =>
        AccessRoleTemplates.All.FirstOrDefault(t => t.Key == r.TemplateKey)?.IsLegacy == true;

    /// <summary>Company administrator first, then the three templates for the new way of working, then the
    /// company's own roles, and today's two transition roles last.</summary>
    private static int RoleOrder(AccessRole r) => r.TemplateKey switch
    {
        AccessRoleTemplates.CompanyAdmin => 0,
        AccessRoleTemplates.StructureOperator => 1,
        AccessRoleTemplates.Timekeeper => 2,
        AccessRoleTemplates.Observer => 3,
        null => 4,
        AccessRoleTemplates.LegacyManager => 5,
        _ => 6,
    };

    private IActionResult? ValidateRole(AccessRoleRequest request, EffectiveAccess me)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length is < 2 or > NameMax)
            return BadRequest(new { error = "NameRequired" });
        if (request.Description is { Length: > DescriptionMax })
            return BadRequest(new { error = "DescriptionTooLong" });
        var permissions = request.Permissions ?? [];
        if (permissions.Count == 0)
            return BadRequest(new { error = "NoPermissions" });
        foreach (var p in permissions)
        {
            if (!AccessPermissions.All.Contains(p))
                return BadRequest(new { error = "UnknownPermission", permission = p });
            // A role's maker defines only power they hold over the whole company themselves.
            if (!me.HasAtCompany(p))
                return Refused("PermissionNotHeld", p);
        }
        return null;
    }

    private static string? Clean(string? text, int max)
    {
        var t = text?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length <= max ? t : t[..max];
    }

    private Task<int> ActiveCountAsync(Guid roleId, CancellationToken ct) =>
        _db.RoleAssignments.CountAsync(a => a.AccessRoleId == roleId && a.RevokedAtUtc == null, ct);

    private void Audit(AuditEventType type, Guid employeeId, string reason) =>
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employeeId,
            EventType = type,
            Reason = reason.Length <= 1000 ? reason : reason[..1000],
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

    /// <summary>Saves, turning a lost race on a unique index (two clicks, two tabs) into a 409 instead of a 500.</summary>
    private async Task<IActionResult?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "AlreadyHeld" });
        }
    }

    private ObjectResult Refused(string error, string? permission = null) =>
        StatusCode(StatusCodes.Status403Forbidden, new { error, permission });

    private ObjectResult NotDuringSupport() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });
}
