using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// VAPID (RFC 8292): identifies this server to a push service with a short-lived ES256 JWT, sent as
/// <c>Authorization: vapid t=&lt;jwt&gt;, k=&lt;public key&gt;</c>. One token per push service origin, signed once and
/// reused for hours instead of once per message.
/// </summary>
public sealed class VapidSigner : IDisposable
{
    /// <summary>How long a token is valid. Push services refuse more than 24 hours.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    /// <summary>A cached token is replaced once less than this much of it is left, so no request ever carries a
    /// token that could expire on the way or under a push service whose clock runs ahead.</summary>
    public static readonly TimeSpan RenewBefore = TimeSpan.FromHours(6);

    private readonly ECDsa _key;
    private readonly string _publicKey;
    private readonly string _subject;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _tokens = new();

    /// <param name="publicKey">VAPID public key, base64url, 65-byte uncompressed point.</param>
    /// <param name="privateKey">VAPID private key, base64url, 32 bytes.</param>
    /// <param name="subject">A mailto: or https: contact for the push service operator.</param>
    public VapidSigner(string publicKey, string privateKey, string subject, TimeProvider? clock = null)
    {
        var pub = WebPushCrypto.Base64UrlDecode(publicKey);
        if (pub.Length != 65 || pub[0] != 0x04)
            throw new ArgumentException("VAPID public key must be a 65-byte uncompressed P-256 point.", nameof(publicKey));
        var priv = WebPushCrypto.LeftPad(WebPushCrypto.Base64UrlDecode(privateKey), 32);

        _key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = priv,
            Q = new ECPoint { X = pub[1..33], Y = pub[33..] },
        });
        _publicKey = WebPushCrypto.Base64UrlEncode(pub);
        _subject = subject;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The <c>Authorization</c> header value for a push to <paramref name="endpoint"/>.</summary>
    public string AuthorizationFor(Uri endpoint) => $"vapid t={TokenFor(Audience(endpoint))}, k={_publicKey}";

    /// <summary>The JWT audience: the push resource's origin (scheme, host, and the port when it is not the default).</summary>
    public static string Audience(Uri endpoint) =>
        endpoint.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);

    /// <summary>A valid token for <paramref name="audience"/>, from the cache while it has long enough left.</summary>
    public string TokenFor(string audience)
    {
        var now = _clock.GetUtcNow();
        if (_tokens.TryGetValue(audience, out var cached) && cached.Expires - now > RenewBefore)
            return cached.Token;

        var expires = now + Lifetime;
        var token = Sign(audience, expires);
        _tokens[audience] = (token, expires);
        return token;
    }

    private string Sign(string audience, DateTimeOffset expires)
    {
        var header = WebPushCrypto.Base64UrlEncode("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"u8);
        var claims = WebPushCrypto.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["sub"] = _subject,
        }));
        var signingInput = $"{header}.{claims}";
        // ES256 is the raw r || s pair (IEEE P1363), not DER — the default for ECDsa.SignData.
        var signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{WebPushCrypto.Base64UrlEncode(signature)}";
    }

    public void Dispose() => _key.Dispose();
}
