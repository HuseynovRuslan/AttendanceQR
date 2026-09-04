using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Domain;

/// <summary>The distinct powers an operator role can hold. READS (dashboards, lists, health, audit) are
/// open to every operator and are not modelled here — only the mutating powers are gated.</summary>
public enum OperatorPermission
{
    /// <summary>Create / disable companies, change branding + plans + prices.</summary>
    ManageTenants,

    /// <summary>Record payments (mark a bill paid/unpaid).</summary>
    Billing,

    /// <summary>Help users back in: reset PIN, reactivate, revoke sessions.</summary>
    ManageUsers,

    /// <summary>Impersonate a tenant admin for support.</summary>
    Impersonate,

    /// <summary>Broadcast a platform-wide announcement.</summary>
    Announce,

    /// <summary>Set other operators' roles.</summary>
    ManageTeam,

    /// <summary>
    /// READ the platform's own commercial data: plans, prices, invoices, the operator team, the
    /// operator audit log.
    ///
    /// The only READ permission here, and it exists for one reason: every other read on /api/super is
    /// open to any operator, which was correct while every operator worked for the platform. GroupHead
    /// does not — they are the customer — so the business side needs a gate of its own. Attendance
    /// reads (the HQ board, tenant dashboards, staff) stay open: that is their own data.
    /// </summary>
    ViewBusiness,
}

/// <summary>The single place that maps an operator role to its powers. One switch, so a new endpoint's
/// gate and the panel's UI can never drift apart.</summary>
public static class OperatorAccess
{
    public static bool Allows(OperatorRoleType role, OperatorPermission perm) => role switch
    {
        OperatorRoleType.Full => true,
        OperatorRoleType.Support => perm is OperatorPermission.ManageUsers or OperatorPermission.Impersonate
                                        or OperatorPermission.ViewBusiness,
        OperatorRoleType.Billing => perm is OperatorPermission.Billing or OperatorPermission.ViewBusiness,
        // GroupHead: no mutation at all, and NOT ViewBusiness — the customer's own attendance only.
        _ => false,
    };

    /// <summary>The permissions a role holds — handed to the frontend (via /me) so it can hide the
    /// actions an operator can't perform, matching the server gate exactly.</summary>
    public static IReadOnlyList<OperatorPermission> PermissionsFor(OperatorRoleType role) =>
        AllPermissions.Where(p => Allows(role, p)).ToList();

    private static readonly OperatorPermission[] AllPermissions =
        (OperatorPermission[])Enum.GetValues(typeof(OperatorPermission));

    /// <summary>
    /// Would setting <paramref name="changingId"/> to <paramref name="newRole"/> leave NO operator with
    /// the Full role? Only Full holds ManageTeam, so an empty Full set means nobody can ever change a role
    /// again — a lockout only a redeploy or DB edit could undo. An allowlisted operator with no profile
    /// row counts as Full (that is the default), so this folds the allowlist over the profiled roles
    /// rather than counting <c>OperatorProfiles</c> rows. The team endpoint calls this under a serialized
    /// section so two concurrent demotions can't each see the other as still-Full and both proceed.
    /// </summary>
    public static bool WouldLeaveNoFull(
        IEnumerable<Guid> operatorIds,
        IReadOnlyDictionary<Guid, OperatorRoleType> profiledRoles,
        Guid changingId,
        OperatorRoleType newRole)
    {
        foreach (var id in operatorIds)
        {
            var role = id == changingId
                ? newRole
                : profiledRoles.TryGetValue(id, out var r) ? r : OperatorRoleType.Full;
            if (role == OperatorRoleType.Full)
                return false; // at least one Full remains
        }
        return true;
    }
}
