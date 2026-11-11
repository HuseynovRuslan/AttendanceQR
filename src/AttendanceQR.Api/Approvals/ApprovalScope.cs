using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Approvals;

/// <summary>
/// Who may ask, who decides, and who may act directly, for one company at one moment. It is loaded per request
/// and read from the database, never from the token, so a role taken away stops working on the next press.
///
/// The answers come from the permission model (RoleAssignments) in every AuthzMode. Requests are new, so there
/// is no «today's rule» for them to mirror. A Legacy company is not an exception either: a timekeeper whose old
/// Manager role still lets them reach the manager desk is held to the request in an approval area all the same
/// (<see cref="ApprovalGate"/>). That is the owner's rule 3 of 2026-11-11: Legacy must not be a way around it.
///
/// The rules, in order:
/// <list type="bullet">
/// <item>A branch is in an approval area when it belongs to a structure, and that structure or the branch itself
/// is switched on. A branch with no structure never is (rule 1). Its changes stay direct, and nobody else is asked.</item>
/// <item>A person asks («requests.create») where they hold that right, and only inside an area.</item>
/// <item>A person decides a request when they hold «requests.decide» through an assignment to the request's
/// structure or branch, or when they are an Operator standing in for that structure right now
/// (<see cref="ApproverSubstitution"/>, rule 6). Holding the right company-wide is not enough on its own: the
/// Company administrator is not the daily approver. Nobody decides their own request.</item>
/// <item>A person acts directly in an area when they hold «requests.decide» there at any level: the operator, and
/// the Company administrator. Their change is recorded as Direct.</item>
/// <item>A person sees a request they made, or one at a branch where they hold any of the three request rights.
/// Seeing never turns into deciding: an observer holds «requests.view» and nothing else.</item>
/// </list>
/// </summary>
public sealed class ApprovalScope
{
    public sealed record BranchInfo(Guid Id, string Name, Guid? StructureId, bool IsActive);

    private readonly AccessGraph _graph;
    private readonly Dictionary<Guid, BranchInfo> _branches;
    private readonly HashSet<Guid> _liveStructures;
    private readonly HashSet<Guid> _liveBranches;
    private readonly List<ApproverSubstitution> _standIns;

    private ApprovalScope(AccessGraph graph, Dictionary<Guid, BranchInfo> branches, HashSet<Guid> liveStructures,
        HashSet<Guid> liveBranches, List<ApproverSubstitution> standIns)
    {
        _graph = graph;
        _branches = branches;
        _liveStructures = liveStructures;
        _liveBranches = liveBranches;
        _standIns = standIns;
    }

    /// <summary>The current company's areas, stand-ins, branches and assignments. A handful of small queries, all
    /// narrowed to the company by its query filter.</summary>
    public static async Task<ApprovalScope> LoadAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var graph = await AccessGraph.LoadAsync(db, ct);
        var branches = await db.Locations.AsNoTracking()
            .Select(l => new BranchInfo(l.Id, l.Name, l.StructureId, l.IsActive))
            .ToDictionaryAsync(b => b.Id, ct);
        var areas = await db.ApprovalAreas.AsNoTracking()
            .Where(a => a.DeactivatedAtUtc == null)
            .Select(a => new { a.Scope, a.StructureId, a.LocationId })
            .ToListAsync(ct);
        var standIns = await db.ApproverSubstitutions.AsNoTracking()
            .Where(s => s.RevokedAtUtc == null && s.StartsAtUtc <= nowUtc && s.EndsAtUtc > nowUtc)
            .ToListAsync(ct);
        return new ApprovalScope(
            graph, branches,
            areas.Where(a => a.Scope == AccessScopeType.Structure && a.StructureId != null).Select(a => a.StructureId!.Value).ToHashSet(),
            areas.Where(a => a.Scope == AccessScopeType.Branch && a.LocationId != null).Select(a => a.LocationId!.Value).ToHashSet(),
            standIns);
    }

    public BranchInfo? Branch(Guid locationId) => _branches.GetValueOrDefault(locationId);

    public IEnumerable<BranchInfo> Branches => _branches.Values;

    public EffectiveAccess AccessOf(Guid employeeId) => _graph.For(employeeId);

    /// <summary>Changes at this branch go through requests.</summary>
    public bool InArea(Guid locationId) =>
        Branch(locationId) is { StructureId: Guid structure }
        && (_liveStructures.Contains(structure) || _liveBranches.Contains(locationId));

    public bool MayRequest(Guid callerId, Guid locationId) =>
        InArea(locationId) && AccessOf(callerId).Covers(AccessPermissions.RequestsCreate, locationId);

    /// <summary>May make the change at this branch themselves, inside an area, recorded as Direct.</summary>
    public bool MayActDirectly(Guid callerId, Guid locationId) =>
        AccessOf(callerId).Covers(AccessPermissions.RequestsDecide, locationId);

    /// <summary>Decides the requests of this branch: «requests.decide» through the branch's structure or the branch
    /// itself, or a Company administrator's live stand-in for the structure. Self-approval is refused separately,
    /// because it depends on the request rather than the branch.</summary>
    public bool IsApprover(Guid callerId, Guid locationId) =>
        AccessOf(callerId).Grants.Any(g =>
            g.Scope != AccessScopeType.Company
            && g.Permissions.Contains(AccessPermissions.RequestsDecide)
            && g.CoversBranch(locationId))
        || StandsIn(callerId, locationId);

    /// <summary>A live stand-in for the branch's structure, held by somebody who is still an Operator. A stand-in
    /// given to somebody who has since lost the role is worth nothing. It reaches deciding and reading the
    /// structure's requests, and nothing else: <see cref="MayActDirectly"/> and <see cref="MayRequest"/> do not
    /// read it.</summary>
    public bool StandsIn(Guid callerId, Guid locationId) =>
        Branch(locationId) is { StructureId: Guid structure }
        && _standIns.Any(s => s.StructureId == structure && s.SubstituteEmployeeId == callerId)
        && IsOperator(callerId);

    /// <summary>An Operator: holds «requests.decide» through an assignment to a structure or a branch. The Company
    /// administrator's company-wide right does not make one, and neither does «requests.view» (an observer).</summary>
    public bool IsOperator(Guid employeeId) =>
        AccessOf(employeeId).Grants.Any(g =>
            g.Scope != AccessScopeType.Company && g.Permissions.Contains(AccessPermissions.RequestsDecide));

    /// <summary>Decides this structure's requests through an assignment of their own (not a stand-in).</summary>
    public bool DecidesStructure(Guid employeeId, Guid structureId) =>
        AccessOf(employeeId).Grants.Any(g =>
            g.Scope == AccessScopeType.Structure && g.StructureId == structureId
            && g.Permissions.Contains(AccessPermissions.RequestsDecide));

    /// <summary>Everybody who decides this branch's requests now. These are the people notified about them. The
    /// caller removes anybody inactive and the person who asked.</summary>
    public IReadOnlyList<Guid> ApproversOf(Guid locationId) =>
        _graph.People.Concat(_standIns.Select(s => s.SubstituteEmployeeId))
            .Distinct()
            .Where(p => IsApprover(p, locationId))
            .ToList();

    public bool MayView(Guid callerId, ChangeRequest request) =>
        request.RequestedByEmployeeId == callerId || MayViewBranch(callerId, request.LocationId);

    private bool MayViewBranch(Guid callerId, Guid locationId)
    {
        var access = AccessOf(callerId);
        return access.Covers(AccessPermissions.RequestsView, locationId)
               || access.Covers(AccessPermissions.RequestsDecide, locationId)
               || access.Covers(AccessPermissions.RequestsCreate, locationId)
               || StandsIn(callerId, locationId);
    }

    /// <summary>The branches whose requests the caller sees. Null means every branch of the company. The caller's
    /// own requests are visible whatever this says.</summary>
    public IReadOnlySet<Guid>? VisibleBranches(Guid callerId)
    {
        var access = AccessOf(callerId);
        if (access.HasAtCompany(AccessPermissions.RequestsView)
            || access.HasAtCompany(AccessPermissions.RequestsDecide)
            || access.HasAtCompany(AccessPermissions.RequestsCreate))
            return null;
        return _branches.Keys.Where(b => MayViewBranch(callerId, b)).ToHashSet();
    }

    /// <summary>The branches whose pending requests wait for this person: their queue.</summary>
    public IReadOnlySet<Guid> ApproverBranches(Guid callerId) =>
        _branches.Keys.Where(b => IsApprover(callerId, b)).ToHashSet();

    /// <summary>The people who would decide in this area if it were switched on: for a structure, the holders of
    /// «requests.decide» through that structure; for a branch, the branch's approvers. The caller drops anybody
    /// inactive. An area with nobody left would collect requests that wait forever, so it is not switched on.</summary>
    public IReadOnlyList<Guid> ApproverCandidates(AccessScopeType scope, Guid? structureId, Guid? locationId) => scope switch
    {
        AccessScopeType.Structure => _graph.People.Where(p => AccessOf(p).Grants.Any(g =>
            g.Scope == AccessScopeType.Structure && g.StructureId == structureId
            && g.Permissions.Contains(AccessPermissions.RequestsDecide))).ToList(),
        AccessScopeType.Branch when locationId is Guid b => ApproversOf(b),
        _ => [],
    };
}
