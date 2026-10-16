namespace AttendanceQR.Api;

/// <summary>
/// The permission an endpoint stands for in the permission model (docs/strukturlar-ve-selahiyyetler.md §3.3).
///
/// Until a company is switched to Enforced this decides nothing: today's [Authorize(Roles)] and the scope
/// checks inside the action still do. In Shadow mode AuthzShadowMiddleware asks both and records every
/// disagreement. A test (PermissionCoverageTests) holds every Admin or Manager endpoint to having one.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class PermissionAttribute : Attribute
{
    public PermissionAttribute(string key) => Key = key;

    public string Key { get; }

    /// <summary>The endpoint works on the whole company at once — a company-wide list, an aggregate, a
    /// company-level setting — so the permission must be held company-wide.</summary>
    public bool AtCompany { get; init; }

    /// <summary>Today's gate is not an [Authorize(Roles)] attribute but a check inside the action or its
    /// service; these are the roles it lets through.</summary>
    public string? LegacyRoles { get; init; }

    /// <summary>The other role has its own route doing the same thing (the admin and manager twins), so a gate
    /// difference here is not a difference in what anybody can do. Recorded as «Twin», not «Gate».</summary>
    public bool Twin { get; init; }

    /// <summary>Admin-only today, but the action narrows its own answer to the caller's area
    /// (LocationScopeRules.ScopeAsync) — so a company switched to Enforced opens it to whoever holds the
    /// permission anywhere. Without it an admin-only endpoint stays the Company administrator's in Enforced
    /// (PermissionGateHandler), because it is the company-wide shape of something.</summary>
    public bool Scoped { get; init; }
}
