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
}

/// <summary>The single place that maps an operator role to its powers. One switch, so a new endpoint's
/// gate and the panel's UI can never drift apart.</summary>
public static class OperatorAccess
{
    public static bool Allows(OperatorRoleType role, OperatorPermission perm) => role switch
    {
        OperatorRoleType.Full => true,
        OperatorRoleType.Support => perm is OperatorPermission.ManageUsers or OperatorPermission.Impersonate,
        OperatorRoleType.Billing => perm is OperatorPermission.Billing,
        _ => false,
    };

    /// <summary>The permissions a role holds — handed to the frontend (via /me) so it can hide the
    /// actions an operator can't perform, matching the server gate exactly.</summary>
    public static IReadOnlyList<OperatorPermission> PermissionsFor(OperatorRoleType role) =>
        AllPermissions.Where(p => Allows(role, p)).ToList();

    private static readonly OperatorPermission[] AllPermissions =
        (OperatorPermission[])Enum.GetValues(typeof(OperatorPermission));
}
