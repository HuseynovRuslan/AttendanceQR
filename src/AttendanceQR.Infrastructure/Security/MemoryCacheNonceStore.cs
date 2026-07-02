using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// In-memory replay guard. Sufficient for a single server; a distributed cache would be
/// needed behind multiple instances. Entries live for the token TTL — once a token would
/// expire anyway, remembering its nonce no longer adds protection.
/// </summary>
public sealed class MemoryCacheNonceStore : INonceStore
{
    private const string KeyPrefix = "qr-nonce:";

    private readonly IMemoryCache _cache;
    private readonly QrTokenOptions _options;
    private readonly object _gate = new();

    public MemoryCacheNonceStore(IMemoryCache cache, IOptions<QrTokenOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    public bool TryConsume(string nonce)
    {
        var key = KeyPrefix + nonce;

        // IMemoryCache has no atomic check-and-add, so serialize the read-then-write.
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out _))
                return false;

            _cache.Set(key, true, TimeSpan.FromSeconds(_options.TtlSeconds));
            return true;
        }
    }
}
