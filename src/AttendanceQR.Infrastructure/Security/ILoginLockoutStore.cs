namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// Brute-force guard for login. A 4-digit PIN has only 10,000 combinations, so without this,
/// an unthrottled attacker could exhaust the whole space in seconds.
/// </summary>
public interface ILoginLockoutStore
{
    /// <summary>True if this account is currently locked out from too many recent failures.</summary>
    bool IsLockedOut(string email);

    /// <summary>Record a failed attempt; locks the account once the threshold is reached.</summary>
    void RecordFailure(string email);

    /// <summary>Clear any recorded failures — call on a successful login.</summary>
    void RecordSuccess(string email);
}
