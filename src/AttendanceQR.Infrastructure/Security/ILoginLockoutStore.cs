namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// Brute-force guard for login. A 4-digit PIN has only 10,000 combinations, so without this,
/// an unthrottled attacker could exhaust the whole space in seconds.
///
/// The <c>key</c> is an opaque bucket id — this store does not know what an account is, so it cannot
/// tell that two spellings of one phone number are the same person. The CALLER must pass a canonical,
/// tenant-scoped key (see <c>LoginIdentity.LockoutKey</c>); passing raw user input instead hands the
/// attacker a fresh budget per spelling and defeats the guard entirely.
/// </summary>
public interface ILoginLockoutStore
{
    /// <summary>How long a lockout lasts, in minutes — surfaced to the user so "wait" has a number.</summary>
    int LockoutMinutes { get; }

    /// <summary>True if this key is currently locked out from too many recent failures.</summary>
    bool IsLockedOut(string key);

    /// <summary>Record a failed attempt; locks the key once the threshold is reached. Returns how many
    /// attempts remain before lockout (0 = this attempt just triggered the lock).</summary>
    int RecordFailure(string key);

    /// <summary>Clear any recorded failures — call on a successful login.</summary>
    void RecordSuccess(string key);
}
