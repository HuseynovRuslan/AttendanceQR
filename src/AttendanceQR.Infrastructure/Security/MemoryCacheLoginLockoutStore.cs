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

    public bool IsLockedOut(string email) => _cache.TryGetValue(LockKey(email), out _);

    public void RecordFailure(string email)
    {
        lock (_gate)
        {
            var key = FailureKey(email);
            var count = _cache.TryGetValue(key, out int existing) ? existing : 0;
            count++;

            if (count >= MaxAttempts)
            {
                _cache.Set(LockKey(email), true, LockoutDuration);
                _cache.Remove(key);
            }
            else
            {
                _cache.Set(key, count, FailureWindow);
            }
        }
    }

    public void RecordSuccess(string email)
    {
        _cache.Remove(FailureKey(email));
        _cache.Remove(LockKey(email));
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
    private static string FailureKey(string email) => FailurePrefix + Normalize(email);
    private static string LockKey(string email) => LockPrefix + Normalize(email);
}
