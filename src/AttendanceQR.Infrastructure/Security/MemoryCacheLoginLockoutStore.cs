using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// In-memory login lockout. Sufficient for a single server; a distributed cache would be needed
/// behind multiple instances. State resets on process restart — an accepted trade-off for staying
/// dependency-free, since restarts are infrequent and this only needs to blunt scripted brute force,
/// not survive a determined, patient attacker.
///
/// One bucket per key holds everything about it — attempts spent, the lock, how many locks it has
/// earned lately — and every decision about a bucket is made under one lock, against timestamps from
/// the injected clock. The cache entry's own expiry only tidies up; it decides nothing, which is what
/// lets a test walk the clock through a day of lockouts without waiting for one.
/// </summary>
public sealed class MemoryCacheLoginLockoutStore : ILoginLockoutStore
{
    private const string Prefix = "login-guard:";
    // Deliberately forgiving: real employees fat-finger a 4-digit PIN. 8 tries then a short 5-minute
    // cool-off still throttles scripted brute force to a crawl, without a 15-minute wall for someone
    // who simply mistyped — the annoyance that turns into a support call.
    private const int MaxAttempts = 8;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    // ...but a flat 5 minutes forever is 8 guesses every 5 minutes, ~2,300 a day: a 4-digit PIN falls in
    // a few days to anyone patient. So the lock grows when the same key keeps earning it. The first lock is
    // the fat-fingered employee and stays short; the third within a day is nobody's typing. A success
    // clears the history, and so does a quiet day.
    private static readonly TimeSpan[] LockoutLadder =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];
    private static readonly TimeSpan StrikeWindow = TimeSpan.FromHours(24);

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public MemoryCacheLoginLockoutStore(IMemoryCache cache, TimeProvider? clock = null)
    {
        _cache = cache;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Mutable on purpose and only ever touched under <see cref="_gate"/>.</summary>
    private sealed class Bucket
    {
        public int Spent;
        public DateTimeOffset SpentExpiresAt;
        public DateTimeOffset LockedUntil;
        public int Strikes;
        public DateTimeOffset StrikesExpireAt;
    }

    public LoginAttempt BeginAttempt(string key)
    {
        var now = _clock.GetUtcNow();
        var cacheKey = CacheKey(key);
        lock (_gate)
        {
            var bucket = _cache.TryGetValue(cacheKey, out Bucket? existing) && existing is not null
                ? existing
                : new Bucket();

            // Locked: refused before any work, and a refusal spends nothing — it is not a guess.
            if (bucket.LockedUntil > now)
                return new LoginAttempt(false, 0, MinutesUntil(bucket.LockedUntil, now));

            if (bucket.SpentExpiresAt <= now)
                bucket.Spent = 0;
            if (bucket.StrikesExpireAt <= now)
                bucket.Strikes = 0;

            bucket.Spent++;
            if (bucket.Spent >= MaxAttempts)
            {
                // The last attempt of the budget locks the key HERE, before its own PIN is even checked —
                // so nothing arriving while it hashes can get in behind it. If it turns out to be the
                // right PIN, RecordSuccess lifts the lock again.
                bucket.Strikes++;
                var duration = LockoutLadder[Math.Min(bucket.Strikes, LockoutLadder.Length) - 1];
                bucket.LockedUntil = now + duration;
                bucket.StrikesExpireAt = now + StrikeWindow;
                bucket.Spent = 0;
                Store(cacheKey, bucket, now);
                return new LoginAttempt(true, 0, MinutesUntil(bucket.LockedUntil, now));
            }

            bucket.SpentExpiresAt = now + FailureWindow;
            Store(cacheKey, bucket, now);
            return new LoginAttempt(true, MaxAttempts - bucket.Spent, 0);
        }
    }

    public bool IsLockedOut(string key)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
            return _cache.TryGetValue(CacheKey(key), out Bucket? bucket) && bucket is not null
                   && bucket.LockedUntil > now;
    }

    public void RecordSuccess(string key)
    {
        lock (_gate)
            _cache.Remove(CacheKey(key));
    }

    /// <summary>Kept only as long as the longest thing it still remembers.</summary>
    private void Store(string cacheKey, Bucket bucket, DateTimeOffset now)
    {
        var until = new[] { bucket.SpentExpiresAt, bucket.LockedUntil, bucket.StrikesExpireAt }.Max();
        var ttl = until - now;
        _cache.Set(cacheKey, bucket, ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(1));
    }

    // Rounded up, and never 0: "0 dəqiqə sonra" tells a locked-out employee nothing.
    private static int MinutesUntil(DateTimeOffset until, DateTimeOffset now)
        => Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));

    // Defensive only — the caller is responsible for canonicalizing the account identity (see the
    // interface docs). Lowercasing here cannot merge "0501234567" and "+994 50 123 45 67".
    private static string CacheKey(string key) => Prefix + key.Trim().ToLowerInvariant();
}
