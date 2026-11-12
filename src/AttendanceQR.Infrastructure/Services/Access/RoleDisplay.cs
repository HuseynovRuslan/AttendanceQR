using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services.Access;

/// <summary>
/// The name a person's role is SHOWN under — display only, decided here once for every screen and the operator
/// console. It changes no right: the gates still read Employee.Role and the role assignments.
///
/// The owner's hierarchy of 2026-11-12, Company Admin → Structure Manager → Timekeeper → Employee, read from
/// what the data already says rather than from anybody's name:
/// <list type="bullet">
/// <item><b>companyAdmin</b> — Role == Admin (or the Company administrator role over the company).</item>
/// <item><b>structureManager</b> — holds the structure_operator role over a structure or a branch.</item>
/// <item><b>timekeeper</b> — holds the timekeeper role; or is one of today's branch managers whose every managed
/// branch lies inside a Structure Manager's reach — the managers of a group that has a Structure Manager.</item>
/// <item><b>manager</b> — a branch manager anywhere else: a company that has no Structure Manager keeps its
/// managers under their old name, so nobody's title changes until their company adopts the hierarchy.</item>
/// <item><b>employee</b> — everybody else.</item>
/// </list>
/// The keys are stable codes; the words are the client's (frontend/src/lib/roleNames.ts).
/// </summary>
public static class RoleDisplay
{
    public const string CompanyAdmin = "companyAdmin";
    public const string StructureManager = "structureManager";
    public const string Timekeeper = "timekeeper";
    public const string Manager = "manager";
    public const string Employee = "employee";

    /// <param name="Hierarchy">The company has at least one Structure Manager — its role pickers say Timekeeper.</param>
    public sealed record Labels(bool Hierarchy, IReadOnlyDictionary<Guid, string> ByEmployee)
    {
        public string For(Guid employeeId, EmployeeRole role) =>
            ByEmployee.TryGetValue(employeeId, out var key) ? key : Default(role);
    }

    /// <summary>What a person is shown as when nothing more is known about them.</summary>
    public static string Default(EmployeeRole role) => role switch
    {
        EmployeeRole.Admin => CompanyAdmin,
        EmployeeRole.Manager => Manager,
        _ => Employee,
    };

    /// <summary>One person, given their role, their live grants, the branches they manage today, and every branch
    /// that is inside some Structure Manager's reach in their company.</summary>
    public static string Of(
        EmployeeRole role, EffectiveAccess access, IReadOnlyCollection<Guid> managed, IReadOnlySet<Guid> structureManagerBranches)
    {
        if (role == EmployeeRole.Admin
            || access.Grants.Any(g => g.TemplateKey == AccessRoleTemplates.CompanyAdmin && g.Scope == AccessScopeType.Company))
            return CompanyAdmin;
        if (LocationScopeRules.IsStructureManager(access))
            return StructureManager;
        if (access.Grants.Any(g => g.TemplateKey == AccessRoleTemplates.Timekeeper && g.Scope != AccessScopeType.Company))
            return Timekeeper;
        if (role == EmployeeRole.Manager)
            return managed.Count > 0 && managed.All(structureManagerBranches.Contains) ? Timekeeper : Manager;
        return Employee;
    }

    /// <summary>Every branch inside some Structure Manager's reach.</summary>
    public static HashSet<Guid> StructureManagerBranches(AccessGraph graph) =>
        graph.People.Select(graph.For)
            .SelectMany(a => a.Grants)
            .Where(g => g.TemplateKey == AccessRoleTemplates.StructureOperator && g.Scope != AccessScopeType.Company)
            .SelectMany(g => g.LocationIds)
            .ToHashSet();

    /// <summary>The labels of these people, from rows already read (the operator console's path).</summary>
    public static Labels Compute(
        AccessGraph graph, IEnumerable<(Guid Id, EmployeeRole Role)> people, IReadOnlyDictionary<Guid, List<Guid>> managed)
    {
        var reach = StructureManagerBranches(graph);
        var map = new Dictionary<Guid, string>();
        foreach (var (id, role) in people)
            map[id] = Of(role, graph.For(id), managed.TryGetValue(id, out var m) ? m : [], reach);
        return new Labels(reach.Count > 0, map);
    }

    /// <summary>The labels of the current company's people who are not plain staff by default — the admins, the
    /// managers, and anybody holding a role — through the company's query filter. Plain staff are left out: their
    /// label is <see cref="Default"/>, and a list of every employee id has no business in this answer.</summary>
    public static async Task<Labels> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var graph = await AccessGraph.LoadAsync(db, ct);
        var holders = graph.People.ToList();
        var people = await db.Employees.AsNoTracking()
            .Where(e => e.Role != EmployeeRole.Employee || holders.Contains(e.Id))
            .Select(e => new { e.Id, e.Role })
            .ToListAsync(ct);
        var managed = (await db.ManagedLocations.AsNoTracking()
                .Select(m => new { m.EmployeeId, m.LocationId })
                .ToListAsync(ct))
            .GroupBy(m => m.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(m => m.LocationId).ToList());
        return Compute(graph, people.Select(p => (p.Id, p.Role)), managed);
    }
}
