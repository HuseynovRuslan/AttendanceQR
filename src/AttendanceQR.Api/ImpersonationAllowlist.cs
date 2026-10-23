using System.Security.Claims;

namespace AttendanceQR.Api;

/// <summary>
/// The off switch for support and view sessions.
///
/// Since c098fa1 such a session lasts as long as an ordinary login — the owner's call, because support is
/// needed often and signing in again every hour was the price. What went with the hour was the one thing
/// that ended a session nobody ended. So a borrowed session now answers, on every request, to the same
/// allowlist that let it be opened: take an operator's id off App:SuperAdminEmployeeIds and restart, and
/// every session they hold, in every company, stops on its next request. The borrowed admin's own logins
/// are untouched — a TokenVersion bump would have ended those with it.
///
/// The console already re-reads the list per request (SuperAdminController.IsSuperAdmin). This is the same
/// rule for the sessions the console hands out.
/// </summary>
public static class ImpersonationAllowlist
{
    /// <summary>False for a support or view session whose "imp" operator is no longer on the allowlist,
    /// or names nobody at all. Every session that is not borrowed passes.</summary>
    public static bool StillAllowed(ClaimsPrincipal principal, IReadOnlyCollection<Guid> operatorIds)
    {
        var imp = principal.FindFirstValue("imp");
        if (imp is null)
            return true;
        return Guid.TryParse(imp, out var operatorId) && operatorIds.Contains(operatorId);
    }
}
