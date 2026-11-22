namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// The answer to "may this PIN guess be checked?", decided BEFORE the check runs.
/// </summary>
/// <param name="Allowed">False while the key is locked: refuse without touching the database or the hasher.</param>
/// <param name="Remaining">When allowed: how many further attempts are left if THIS one turns out wrong.
/// 0 means this attempt was the last of the budget and the lock is already set — a wrong answer is a 429.</param>
/// <param name="LockoutMinutes">How long the lock lasts (or, when refused, how much of it is left), in whole
/// minutes rounded up, so "wait" always has a number in it. Meaningful when refused or when Remaining is 0.</param>
public readonly record struct LoginAttempt(bool Allowed, int Remaining, int LockoutMinutes);

/// <summary>
/// Brute-force guard for login. A 4-digit PIN has only 10,000 combinations, so without this,
/// an unthrottled attacker could exhaust the whole space in seconds.
///
/// The <c>key</c> is an opaque bucket id — this store does not know what an account is, so it cannot
/// tell that two spellings of one phone number are the same person. The CALLER must pass a canonical,
/// tenant-scoped key (see <c>LoginIdentity.LockoutKey</c>); passing raw user input instead hands the
/// attacker a fresh budget per spelling and defeats the guard entirely.
///
/// The budget is spent on the way IN, not on the way out. Checking "is it locked?" and recording the
/// failure after a PBKDF2 verify were two separate steps, so every request that arrived while the first
/// ones were still hashing saw an unlocked key — fifty parallel guesses were fifty guesses, whatever the
/// cap said. <see cref="BeginAttempt"/> decides and spends in one step; a correct PIN gives it back.
/// </summary>
public interface ILoginLockoutStore
{
    /// <summary>
    /// Atomically: refuse while the key is locked, otherwise spend one attempt from its budget — call it
    /// BEFORE verifying, and call <see cref="RecordSuccess"/> if the PIN turns out right. The attempt that
    /// empties the budget sets the lock itself, so no request racing it can slip in behind.
    /// </summary>
    LoginAttempt BeginAttempt(string key);

    /// <summary>True if this key is currently locked out. A read for display and tests only — never gate on
    /// it: a check followed later by a write is exactly the race <see cref="BeginAttempt"/> exists to close.</summary>
    bool IsLockedOut(string key);

    /// <summary>Clear the key's spent attempts, its lock and its lockout history — on a successful login,
    /// or when an admin hands out a new PIN.</summary>
    void RecordSuccess(string key);
}
