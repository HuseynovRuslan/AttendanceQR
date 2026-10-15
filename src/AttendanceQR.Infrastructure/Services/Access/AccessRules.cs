using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Infrastructure.Services.Access;

public sealed record GrantDecision(bool Allowed, string? Error = null, string? Permission = null)
{
    public static GrantDecision Ok { get; } = new(true);
    public static GrantDecision No(string error, string? permission = null) => new(false, error, permission);
}

/// <summary>
/// The rules that decide who may act on whom and who may hand out what — the one place they live
/// (docs/strukturlar-ve-selahiyyetler.md §3.5). Pure: everything they need is passed in, so every rule is
/// pinned by a test without a database.
/// </summary>
public static class AccessRules
{
    /// <summary>
    /// Is every power the target holds also the actor's, over the same area? You manage only people below
    /// you or level with you — never somebody who could do something you cannot.
    ///
    /// A grant from a peers-manageable role (today's branch manager) is the exception, kept so nobody loses
    /// today's colleague rule in the move: it is covered when the actor holds that same role at the target's
    /// home branch — a fellow manager of a branch they share — or holds every one of its permissions
    /// company-wide. That is exactly today's rule: a manager reaches a colleague whose own branch is theirs,
    /// and an admin reaches anybody; a manager who merely happens to manage the same branches as somebody
    /// based elsewhere does not.
    /// </summary>
    public static bool Dominates(EffectiveAccess actor, EffectiveAccess target, Guid targetHome)
    {
        foreach (var grant in target.Grants)
        {
            if (grant.PeersManageable)
            {
                var peer = actor.Grants.Any(a => a.RoleId == grant.RoleId && a.CoversBranch(targetHome));
                if (peer || grant.Permissions.All(actor.HasAtCompany))
                    continue;
                return false;
            }

            foreach (var permission in grant.Permissions)
                if (!CoversArea(actor, permission, grant.Scope, grant.StructureId, grant.LocationIds))
                    return false;
        }
        return true;
    }

    /// <summary>
    /// May the actor act on this person — their account or their records — with this permission? Anybody
    /// else: the permission must reach the person's home branch, and the actor must dominate them.
    /// Themselves: never on the account paths (<paramref name="refuseSelf"/> — a PIN, a login number, a
    /// profile are changed through one's own settings, not the desk that manages others; today nobody may);
    /// on their own records only with the permission company-wide (today an admin may correct their own day,
    /// a manager may not). A platform operator's row is never a target where <paramref name="refuseOperators"/>
    /// is set — the credential paths, where a takeover would hand over the console.
    /// </summary>
    public static bool CanActOn(
        EffectiveAccess actor, string permission, Guid targetId, Guid targetHome, EffectiveAccess target,
        bool targetIsOperator, bool refuseOperators, bool refuseSelf)
    {
        if (refuseOperators && targetIsOperator)
            return false;
        if (targetId == actor.EmployeeId)
            return !refuseSelf && actor.HasAtCompany(permission);
        return actor.Covers(permission, targetHome) && Dominates(actor, target, targetHome);
    }

    /// <summary>May the actor see this person's records? Their own always; anybody else's where the permission
    /// reaches the person's home branch, whatever their role.</summary>
    public static bool CanSee(EffectiveAccess actor, string permission, Guid targetId, Guid targetHome) =>
        targetId == actor.EmployeeId || actor.Covers(permission, targetHome);

    /// <summary>
    /// May the actor give this role over this area to this person? Never to themselves; only where they may
    /// assign; only permissions they hold themselves over that whole area; and a company-only permission only
    /// at company scope. Nobody can raise their own power, or hand out power they do not have.
    /// </summary>
    public static GrantDecision CanGrant(
        EffectiveAccess actor, IReadOnlyCollection<string> rolePermissions, AccessScopeType scope,
        Guid? structureId, Guid? locationId, Guid assigneeId, bool assigneeIsOperator)
    {
        if (assigneeId == actor.EmployeeId)
            return GrantDecision.No("SelfGrant");
        if (assigneeIsOperator)
            return GrantDecision.No("CannotManageOperator");
        if (!CoversTarget(actor, AccessPermissions.AssignmentsManage, scope, structureId, locationId))
            return GrantDecision.No("OutOfScope");

        foreach (var permission in rolePermissions)
        {
            if (!AccessPermissions.All.Contains(permission))
                return GrantDecision.No("UnknownPermission", permission);
            if (AccessPermissions.CompanyOnly.Contains(permission) && scope != AccessScopeType.Company)
                return GrantDecision.No("CompanyOnlyPermission", permission);
            if (!CoversTarget(actor, permission, scope, structureId, locationId))
                return GrantDecision.No("PermissionNotHeld", permission);
        }
        return GrantDecision.Ok;
    }

    private static bool CoversTarget(
        EffectiveAccess actor, string permission, AccessScopeType scope, Guid? structureId, Guid? locationId) =>
        scope switch
        {
            AccessScopeType.Company => actor.HasAtCompany(permission),
            AccessScopeType.Structure => structureId is Guid s && actor.CoversStructure(permission, s),
            AccessScopeType.Branch => locationId is Guid l && actor.Covers(permission, l),
            _ => false,
        };

    private static bool CoversArea(
        EffectiveAccess actor, string permission, AccessScopeType scope, Guid? structureId, IReadOnlySet<Guid> locations) =>
        scope switch
        {
            AccessScopeType.Company => actor.HasAtCompany(permission),
            AccessScopeType.Structure => structureId is Guid s && actor.CoversStructure(permission, s),
            AccessScopeType.Branch => locations.All(l => actor.Covers(permission, l)),
            _ => false,
        };
}
