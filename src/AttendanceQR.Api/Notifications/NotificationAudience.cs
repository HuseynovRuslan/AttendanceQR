using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Notifications;

/// <summary>
/// Whose attendance each person may see — the question every attendance notification is a copy of, asked in
/// reverse and in batch: not «may this caller see branch Y» but «for these people, which branches».
///
/// The same rule the board and the reports use (<c>LocationScopeRules.ScopeAsync</c> with «attendance.view»), keyed on
/// the subject's OWN branch exactly as every scope rule in the system is:
///   • Legacy / Shadow — the role decides: a company administrator everywhere, a branch manager at the branches they
///     manage (ManagedLocations), anybody else nowhere.
///   • Enforced — the permission graph decides: «attendance.view» AND «employees.view» over the branch (a click opens
///     the person's card, which asks for the second). The graph is loaded once (<see cref="AccessGraph.LoadAsync"/>),
///     not through the request observer, whose per-scope state would cap later answers in a worker.
///
/// Only ACTIVE accounts are in it, read at the moment of asking — a deactivated person or a revoked grant stops
/// receiving with the next event, not after a cache expires. Built per company: the context's tenant filter is the
/// isolation, and nothing here reads across it.
/// </summary>
public sealed class NotificationAudience
{
    private readonly Dictionary<Guid, ScopeAnswer> _scopes;

    private NotificationAudience(Dictionary<Guid, ScopeAnswer> scopes) => _scopes = scopes;

    public static NotificationAudience Empty { get; } = new([]);

    /// <summary>What this person may see; <see cref="ScopeAnswer.Nowhere"/> when they are not (or no longer) eligible.</summary>
    public ScopeAnswer For(Guid employeeId) => _scopes.GetValueOrDefault(employeeId) ?? ScopeAnswer.Nowhere;

    public bool Covers(Guid viewer, Guid branch) => For(viewer).Covers(branch);

    public static async Task<NotificationAudience> LoadAsync(
        AppDbContext db, IReadOnlyCollection<Guid> viewers, CancellationToken ct = default)
    {
        if (viewers.Count == 0)
            return Empty;

        var people = await db.Employees.AsNoTracking()
            .Where(e => viewers.Contains(e.Id) && e.IsActive)
            .Select(e => new { e.Id, e.Role })
            .ToListAsync(ct);
        if (people.Count == 0)
            return Empty;

        var tenantId = db.CurrentTenantId;
        var mode = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.AuthzMode)
            .FirstOrDefaultAsync(ct);

        var scopes = new Dictionary<Guid, ScopeAnswer>();
        if (mode == AuthzMode.Enforced)
        {
            var graph = await AccessGraph.LoadAsync(db, ct);
            foreach (var person in people)
            {
                var access = graph.For(person.Id);
                var attendance = access.BranchesFor(AccessPermissions.AttendanceView);
                var employees = access.BranchesFor(AccessPermissions.EmployeesView);
                scopes[person.Id] = Intersect(attendance, employees);
            }
            return new NotificationAudience(scopes);
        }

        var managerIds = people.Where(p => p.Role == EmployeeRole.Manager).Select(p => p.Id).ToList();
        var managed = managerIds.Count == 0
            ? []
            : (await db.ManagedLocations.AsNoTracking()
                    .Where(m => managerIds.Contains(m.EmployeeId))
                    .Select(m => new { m.EmployeeId, m.LocationId })
                    .ToListAsync(ct))
                .GroupBy(m => m.EmployeeId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<Guid>)g.Select(m => m.LocationId).ToHashSet());
        foreach (var person in people)
        {
            scopes[person.Id] = person.Role switch
            {
                EmployeeRole.Admin => ScopeAnswer.Everywhere,
                EmployeeRole.Manager => new ScopeAnswer(false, managed.GetValueOrDefault(person.Id) ?? new HashSet<Guid>()),
                _ => ScopeAnswer.Nowhere,
            };
        }
        return new NotificationAudience(scopes);
    }

    /// <summary>Where both permissions reach. Null (from <see cref="EffectiveAccess.BranchesFor"/>) is the whole company.</summary>
    private static ScopeAnswer Intersect(IReadOnlySet<Guid>? a, IReadOnlySet<Guid>? b) => (a, b) switch
    {
        (null, null) => ScopeAnswer.Everywhere,
        (null, { } only) => new ScopeAnswer(false, only),
        ({ } only, null) => new ScopeAnswer(false, only),
        ({ } x, { } y) => new ScopeAnswer(false, x.Where(y.Contains).ToHashSet()),
    };
}
