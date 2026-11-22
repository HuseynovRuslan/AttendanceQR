using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// An int counter in <see cref="IMemoryCache"/> whose read-modify-write cannot interleave.
///
/// The per-IP login caps were written as <c>Set(key, Get(key) + 1)</c> — two requests reading the same
/// value both write value+1, and one of them is never counted. Under a scripted burst that is most of
/// them. Every change here happens under one lock, so a cap is a cap however many requests arrive at once.
/// The values stay plain ints under the same keys, so anything that reads or seeds them (tests do) keeps
/// working. One lock for every counter is fine: the work inside it is a dictionary lookup.
/// </summary>
public static class CacheCounter
{
    private static readonly object Gate = new();

    /// <summary>
    /// Spend one unit unless the counter already stands at <paramref name="cap"/>. True when spent. The
    /// window restarts with every unit, as the old per-failure write did.
    /// </summary>
    public static bool TryTake(IMemoryCache cache, string key, int cap, TimeSpan window)
    {
        lock (Gate)
        {
            var current = cache.TryGetValue(key, out int value) ? value : 0;
            if (current >= cap)
                return false;
            cache.Set(key, current + 1, window);
            return true;
        }
    }

    /// <summary>
    /// Give back up to <paramref name="units"/>, never below zero. A counter that has already expired is
    /// left expired rather than brought back at zero.
    /// </summary>
    public static void GiveBack(IMemoryCache cache, string key, int units, TimeSpan window)
    {
        lock (Gate)
        {
            if (cache.TryGetValue(key, out int value) && value > 0)
                cache.Set(key, Math.Max(0, value - units), window);
        }
    }

    /// <summary>
    /// Forget the counter. Under the same lock as the rest: a bare Remove could land between another
    /// request's read and its write in <see cref="TryTake"/>, and that write would bring the count back.
    /// </summary>
    public static void Clear(IMemoryCache cache, string key)
    {
        lock (Gate)
            cache.Remove(key);
    }
}
