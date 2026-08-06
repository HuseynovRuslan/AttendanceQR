namespace AttendanceQR.Domain.Entities;

/// <summary>What an operator may do. WHO is an operator at all is still the .env allowlist
/// (App__SuperAdminEmployeeIds) — becoming one requires a deploy. This only narrows an existing
/// operator's powers. Stored as int; values are stable, never renumber.</summary>
public enum OperatorRoleType
{
    /// <summary>Everything (default for an allowlisted operator with no profile row).</summary>
    Full = 0,

    /// <summary>Read everything + help users (PIN reset / reactivate / revoke) + impersonate for support.
    /// Cannot create/disable companies, change plans/prices, broadcast, or manage the team.</summary>
    Support = 1,

    /// <summary>Read everything + record payments. Nothing else.</summary>
    Billing = 2,
}

/// <summary>
/// A per-operator role. GLOBAL (no tenant filter, like <see cref="SuperAdminAuditLog"/>): operators are a
/// platform concept, not a tenant's. One row per operator employee id; an allowlisted operator with NO
/// row defaults to <see cref="OperatorRoleType.Full"/>, so adding the profile table takes no access away
/// from anyone who has it today.
/// </summary>
public class OperatorProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The operator's Employee id (unique). Must also be in the .env allowlist to have any access.</summary>
    public Guid EmployeeId { get; set; }

    public OperatorRoleType Role { get; set; } = OperatorRoleType.Full;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
