using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// In-memory login lockout. Sufficient for a single server; a distributed cache would be needed
/// behind multiple instances. State resets on process restart — an accepted trade-off for staying
/// dependency-free, since restarts are infrequent and this only needs to blunt scripted brute force,
/// not survive a determined, patient attacker.
/// </summary>
public sealed class MemoryCacheLoginLockoutStore : ILoginLockoutStore
{
    private const string FailurePrefix = "login-fail:";
    private const string LockPrefix = "login-lock:";
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;
    private readonly object _gate = new();

    public MemoryCacheLoginLockoutStore(IMemoryCache cache) => _cache = cache;

    public bool IsLockedOut(string key) => _cache.TryGetValue(LockKey(key), out _);

    public void RecordFailure(string key)
    {
        lock (_gate)
        {
            var failureKey = FailureKey(key);
            var count = _cache.TryGetValue(failureKey, out int existing) ? existing : 0;
            count++;

            if (count >= MaxAttempts)
            {
                _cache.Set(LockKey(key), true, LockoutDuration);
                _cache.Remove(failureKey);
            }
            else
            {
                _cache.Set(failureKey, count, FailureWindow);
            }
        }
    }

    public void RecordSuccess(string key)
    {
        _cache.Remove(FailureKey(key));
        _cache.Remove(LockKey(key));
    }

    // Defensive only — the caller is responsible for canonicalizing the account identity (see the
    // interface docs). Lowercasing here cannot merge "0501234567" and "+994 50 123 45 67".
    private static string Normalize(string key) => key.Trim().ToLowerInvariant();
    private static string FailureKey(string key) => FailurePrefix + Normalize(key);
    private static string LockKey(string key) => LockPrefix + Normalize(key);
}
