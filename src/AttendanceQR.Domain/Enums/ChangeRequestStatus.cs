namespace AttendanceQR.Domain.Enums;

/// <summary>
/// Where a change request stands. Stored as int — never renumber.
///
/// Pending is the only state that moves. Approved, Rejected and Cancelled are final: a decision is made once
/// (ChangeRequestRules.CanMove), and the database holds the same line (CK_ChangeRequests_* and the one-decision
/// index on ChangeRequestEvents).
/// </summary>
public enum ChangeRequestStatus
{
    Pending = 0,

    /// <summary>Approved AND applied — the two happen in one transaction, so there is no approved-but-not-done.</summary>
    Approved = 1,

    /// <summary>Refused by an approver, with the reason written down.</summary>
    Rejected = 2,

    /// <summary>Withdrawn by the person who made it, before anybody decided.</summary>
    Cancelled = 3,
}
