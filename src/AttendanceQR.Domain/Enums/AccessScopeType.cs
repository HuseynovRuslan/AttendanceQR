namespace AttendanceQR.Domain.Enums;

/// <summary>How far a role assignment reaches. Stored as int — never renumber.</summary>
public enum AccessScopeType
{
    // Every branch of the company, including ones opened later, and the company-wide settings.
    Company = 0,

    // The branches that are in the structure now — a branch moved in or out changes this at once.
    Structure = 1,

    // One branch.
    Branch = 2,
}
