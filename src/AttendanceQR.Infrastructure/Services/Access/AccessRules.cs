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
    public static bool Dominates(EffectiveAccess actor, EffectiveAccess target, Guid targetHome) =>
        DominatesAt(actor, target, targetHome);

    /// <summary>
    /// Strictly above: the actor dominates the target, and the target does not dominate the actor back. The
    /// owner's rule of 2026-10-15 for ACCOUNTS (PIN, login number, profile, roles): a second operator of the
    /// same structure is a colleague, not a subordinate, and neither changes the other's account.
    /// <paramref name="actorHome"/> answers the colleague rule in the reverse direction; unknown, it is read as
    /// «the target is not the actor's colleague-manager».
    /// </summary>
    public static bool Outranks(EffectiveAccess actor, Guid? actorHome, EffectiveAccess target, Guid targetHome) =>
        DominatesAt(actor, target, targetHome) && !DominatesAt(target, actor, actorHome);

    /// <summary>Holds a role of the new model — not only the three that mirror today's Admin and Manager.
    /// Only such a person can be somebody's equal under the strict account rule: today's managers keep
    /// today's colleague rule, and today's admins act on one another as they do now.</summary>
    public static bool HoldsNewRole(EffectiveAccess person) =>
        person.Grants.Any(g => g.TemplateKey is not (AccessRoleTemplates.CompanyAdmin or AccessRoleTemplates.LegacyManager
            or AccessRoleTemplates.PinHelper));

    private static bool DominatesAt(EffectiveAccess actor, EffectiveAccess target, Guid? targetHome)
    {
        foreach (var grant in target.Grants)
        {
            if (grant.PeersManageable)
            {
                var peer = targetHome is Guid home && actor.Grants.Any(a => a.RoleId == grant.RoleId && a.CoversBranch(home));
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
    /// <param name="strict">An ACCOUNT action (PIN, login number, profile): the actor must be strictly above a
    /// target who holds a new-model role — see <see cref="Outranks"/>. Company-wide power is exempt, as today
    /// between admins.</param>
    public static bool CanActOn(
        EffectiveAccess actor, string permission, Guid targetId, Guid targetHome, EffectiveAccess target,
        bool targetIsOperator, bool refuseOperators, bool refuseSelf, bool strict = false, Guid? actorHome = null)
    {
        if (refuseOperators && targetIsOperator)
            return false;
        if (targetId == actor.EmployeeId)
            return !refuseSelf && actor.HasAtCompany(permission);
        if (!actor.Covers(permission, targetHome) || !Dominates(actor, target, targetHome))
            return false;
        return !strict || actor.HasAtCompany(permission) || !HoldsNewRole(target)
               || !DominatesAt(target, actor, actorHome);
    }

    /// <summary>May the actor see this person's records? Their own always; anybody else's where the permission
    /// reaches the person's home branch, whatever their role.</summary>
    public static bool CanSee(EffectiveAccess actor, string permission, Guid targetId, Guid targetHome) =>
        targetId == actor.EmployeeId || actor.Covers(permission, targetHome);

    /// <summary>
    /// May the actor give this role over this area to this person?
    /// - Never to themselves, never to a platform operator.
    /// - Only where they may assign, and only permissions they hold themselves over that whole area; a
    ///   company-only permission only at company scope. Nobody raises their own power or hands out power they
    ///   do not have (§3.5).
    /// - The owner's rules of 2026-10-15, below company level:
    ///   2. only to their own people — somebody whose home branch is inside their assigning area — unless they
    ///      hold the separate company-only right to reach further (<see cref="AccessPermissions.AssignmentsCrossArea"/>);
    ///   3. only a role BELOW their own over that area, never their equal or more. The rights about rights
    ///      (<see cref="AccessPermissions.Meta"/>) are left out of the comparison: an operator handing out the
    ///      operator's daily powers minus the right to assign is still appointing an operator;
    ///   1. only to somebody below them — a second operator of the same structure is a colleague.
    ///   A company-wide assigner is the «company account» the owner meant, and may appoint their equal.
    /// </summary>
    public static GrantDecision CanGrant(
        EffectiveAccess actor, IReadOnlyCollection<string> rolePermissions, AccessScopeType scope,
        Guid? structureId, Guid? locationId, Guid assigneeId, bool assigneeIsOperator,
        Guid assigneeHome, EffectiveAccess assignee, Guid? actorHome)
    {
        if (assigneeId == actor.EmployeeId)
            return GrantDecision.No("SelfGrant");
        if (assigneeIsOperator)
            return GrantDecision.No("CannotManageOperator");
        if (!CoversTarget(actor, AccessPermissions.AssignmentsManage, scope, structureId, locationId))
            return GrantDecision.No("OutOfScope");
        if (!actor.Covers(AccessPermissions.AssignmentsManage, assigneeHome)
            && !actor.HasAtCompany(AccessPermissions.AssignmentsCrossArea))
            return GrantDecision.No("AssigneeOutOfScope");

        foreach (var permission in rolePermissions)
        {
            if (!AccessPermissions.All.Contains(permission))
                return GrantDecision.No("UnknownPermission", permission);
            if (AccessPermissions.CompanyOnly.Contains(permission) && scope != AccessScopeType.Company)
                return GrantDecision.No("CompanyOnlyPermission", permission);
            if (!CoversTarget(actor, permission, scope, structureId, locationId))
                return GrantDecision.No("PermissionNotHeld", permission);
        }

        if (actor.HasAtCompany(AccessPermissions.AssignmentsManage))
            return GrantDecision.Ok;

        var held = AccessPermissions.All
            .Where(p => !AccessPermissions.Meta.Contains(p) && CoversTarget(actor, p, scope, structureId, locationId))
            .ToHashSet();
        var given = rolePermissions.Where(p => !AccessPermissions.Meta.Contains(p)).ToHashSet();
        if (!given.IsProperSubsetOf(held))
            return GrantDecision.No("NotBelowYou");
        if (!Outranks(actor, actorHome, assignee, assigneeHome))
            return GrantDecision.No("AssigneeNotBelowYou");
        return GrantDecision.Ok;
    }

    /// <summary>
    /// May the actor take this assignment away? Only one they could have given — the same rules, so nobody
    /// strips a colleague or somebody above them — and never their own. A system row is never taken away by
    /// hand: it mirrors today's Employee.Role and ManagedLocations, which are what to change instead (the next
    /// reconcile would only put it back).
    /// </summary>
    public static GrantDecision CanRevoke(
        EffectiveAccess actor, bool systemAssignment, IReadOnlyCollection<string> rolePermissions, AccessScopeType scope,
        Guid? structureId, Guid? locationId, Guid assigneeId, bool assigneeIsOperator,
        Guid assigneeHome, EffectiveAccess assignee, Guid? actorHome)
    {
        if (systemAssignment)
            return GrantDecision.No("SystemAssignment");
        if (assigneeId == actor.EmployeeId)
            return GrantDecision.No("SelfRevoke");
        return CanGrant(actor, rolePermissions, scope, structureId, locationId, assigneeId, assigneeIsOperator,
            assigneeHome, assignee, actorHome);
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
