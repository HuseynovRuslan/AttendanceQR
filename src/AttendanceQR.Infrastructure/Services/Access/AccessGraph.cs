using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services.Access;

/// <summary>One live assignment, resolved: the role's permissions and the branches its area covers now.</summary>
/// <param name="LocationIds">Branch scope: that branch. Structure scope: the branches in the structure at the
/// moment of loading. Company scope: empty — company covers every branch by definition.</param>
public sealed record ResolvedGrant(
    Guid RoleId, string? TemplateKey, bool PeersManageable, IReadOnlySet<string> Permissions,
    AccessScopeType Scope, Guid? StructureId, IReadOnlySet<Guid> LocationIds)
{
    public bool CoversBranch(Guid locationId) => Scope == AccessScopeType.Company || LocationIds.Contains(locationId);
}

/// <summary>What one person may do, and where. Computed from the database on every request — never from the
/// token — so a change takes effect on the next request without signing anybody out.</summary>
public sealed class EffectiveAccess
{
    private readonly Dictionary<string, (bool Company, HashSet<Guid> Locations)> _byPermission = new();

    public EffectiveAccess(Guid employeeId, IReadOnlyList<ResolvedGrant> grants)
    {
        EmployeeId = employeeId;
        Grants = grants;
        foreach (var grant in grants)
            foreach (var permission in grant.Permissions)
            {
                if (!_byPermission.TryGetValue(permission, out var entry))
                    entry = (false, []);
                if (grant.Scope == AccessScopeType.Company)
                    entry.Company = true;
                else
                    entry.Locations.UnionWith(grant.LocationIds);
                _byPermission[permission] = entry;
            }
    }

    public Guid EmployeeId { get; }

    public IReadOnlyList<ResolvedGrant> Grants { get; }

    public IEnumerable<string> Permissions => _byPermission.Keys;

    public bool Has(string permission) => _byPermission.ContainsKey(permission);

    public bool HasAtCompany(string permission) =>
        _byPermission.TryGetValue(permission, out var entry) && entry.Company;

    public bool Covers(string permission, Guid locationId) =>
        _byPermission.TryGetValue(permission, out var entry) && (entry.Company || entry.Locations.Contains(locationId));

    /// <summary>Null means the whole company; otherwise the branches (possibly none).</summary>
    public IReadOnlySet<Guid>? BranchesFor(string permission) =>
        !_byPermission.TryGetValue(permission, out var entry) ? new HashSet<Guid>()
        : entry.Company ? null
        : entry.Locations;

    /// <summary>Holds the permission over a whole structure: at company scope, or through an assignment to that
    /// very structure. Holding it at every branch the structure has today is not enough — a branch moved in
    /// tomorrow would not be covered.</summary>
    public bool CoversStructure(string permission, Guid structureId) =>
        HasAtCompany(permission)
        || Grants.Any(g => g.Scope == AccessScopeType.Structure && g.StructureId == structureId && g.Permissions.Contains(permission));
}

/// <summary>
/// Every live assignment of the current company, loaded once and resolved — three small queries, scoped by
/// the company's query filter. Archived roles and revoked assignments grant nothing; unknown permission keys
/// are dropped, never widened.
/// </summary>
public sealed class AccessGraph
{
    private readonly Dictionary<Guid, List<ResolvedGrant>> _byEmployee;

    private AccessGraph(Dictionary<Guid, List<ResolvedGrant>> byEmployee) => _byEmployee = byEmployee;

    public static AccessGraph Empty { get; } = new([]);

    public static async Task<AccessGraph> LoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        var roles = await db.AccessRoles.AsNoTracking()
            .Where(r => r.ArchivedAtUtc == null)
            .ToDictionaryAsync(r => r.Id, ct);
        var assignments = await db.RoleAssignments.AsNoTracking()
            .Where(a => a.RevokedAtUtc == null)
            .ToListAsync(ct);

        var structureIds = assignments.Where(a => a.StructureId != null).Select(a => a.StructureId!.Value).Distinct().ToList();
        var branchesOf = structureIds.Count == 0
            ? new Dictionary<Guid, HashSet<Guid>>()
            : (await db.Locations.AsNoTracking()
                    .Where(l => l.StructureId != null && structureIds.Contains(l.StructureId.Value))
                    .Select(l => new { l.Id, StructureId = l.StructureId!.Value })
                    .ToListAsync(ct))
                .GroupBy(l => l.StructureId)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Id).ToHashSet());

        var byEmployee = new Dictionary<Guid, List<ResolvedGrant>>();
        foreach (var a in assignments)
        {
            if (!roles.TryGetValue(a.AccessRoleId, out var role))
                continue;
            var permissions = role.Permissions.Where(AccessPermissions.All.Contains).ToHashSet();
            IReadOnlySet<Guid> locations = a.Scope switch
            {
                AccessScopeType.Branch when a.LocationId is Guid l => new HashSet<Guid> { l },
                AccessScopeType.Structure when a.StructureId is Guid s => branchesOf.GetValueOrDefault(s) ?? [],
                _ => new HashSet<Guid>(),
            };
            if (!byEmployee.TryGetValue(a.EmployeeId, out var list))
                byEmployee[a.EmployeeId] = list = [];
            list.Add(new ResolvedGrant(role.Id, role.TemplateKey, role.PeersManageable, permissions, a.Scope, a.StructureId, locations));
        }
        return new AccessGraph(byEmployee);
    }

    public EffectiveAccess For(Guid employeeId) =>
        new(employeeId, _byEmployee.TryGetValue(employeeId, out var grants) ? grants : []);

    /// <summary>Everybody who holds at least one live assignment.</summary>
    public IEnumerable<Guid> People => _byEmployee.Keys;
}
