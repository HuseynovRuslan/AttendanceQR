namespace AttendanceQR.Domain.Enums;

/// <summary>
/// Which authorization decides for a company while the permission model is being introduced, company by
/// company (docs/strukturlar-ve-selahiyyetler.md §4.3). Stored as int — never renumber.
/// </summary>
public enum AuthzMode
{
    // Today's roles decide; the permission model only answers /api/me/access.
    Legacy = 0,

    // Today's roles still decide. The permission model decides alongside, and every disagreement is
    // recorded (AuthzShadowEvents) — nobody notices anything.
    Shadow = 1,

    // The permission model decides. Going back is setting this to Shadow; nothing else changes.
    Enforced = 2,
}
