using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Web Push message encryption — RFC 8291 over the RFC 8188 <c>aes128gcm</c> content coding, one record.
///
/// Built on the platform's own primitives (ECDH P-256, HKDF-SHA256, AES-128-GCM) rather than a library. The library
/// this replaces sent the older <c>aesgcm</c> draft coding and wrote the ECDH public key and shared secret as
/// big integers, so whenever one of them happened to start with a zero byte it came out a byte short and the
/// browser could not decrypt the message (about 1 push in 270). Here every key and secret is a fixed 32 bytes per
/// coordinate, padded on the left, and <c>aes128gcm</c> is the one coding every current push service and browser
/// accepts (Safari accepts nothing else).
/// </summary>
public static class WebPushCrypto
{
    /// <summary>The record size written into the header. One record carries the whole message, so a payload
    /// may be up to <see cref="MaxPlaintextLength"/> bytes; push services accept a 4096-byte body.</summary>
    public const int RecordSize = 4096;

    private const int KeyLength = 32;                 // one P-256 coordinate / the shared secret
    private const int PublicKeyLength = 1 + 2 * KeyLength; // uncompressed point: 0x04 || X || Y
    private const int SaltLength = 16;
    private const int TagLength = 16;
    private const int HeaderLength = SaltLength + 4 + 1 + PublicKeyLength;

    /// <summary>Largest plaintext that fits: the whole body (header + record) must stay within 4096 bytes.</summary>
    public const int MaxPlaintextLength = RecordSize - HeaderLength - TagLength - 1;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] CekInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>Encrypts <paramref name="plaintext"/> for one subscription, with a fresh key pair and salt.</summary>
    /// <param name="userAgentPublicKey">The subscription's <c>p256dh</c> key, decoded (65 bytes, uncompressed).</param>
    /// <param name="authSecret">The subscription's <c>auth</c> secret, decoded (16 bytes).</param>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] userAgentPublicKey, byte[] authSecret)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, userAgentPublicKey, authSecret, ephemeral, RandomNumberGenerator.GetBytes(SaltLength));
    }

    /// <summary>The deterministic core: the caller supplies the application server's key pair and the salt. Public
    /// so the RFC 8291 Appendix A vector can be reproduced exactly; production code uses the overload above.</summary>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext, byte[] userAgentPublicKey, byte[] authSecret, ECDiffieHellman serverKey, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(userAgentPublicKey);
        ArgumentNullException.ThrowIfNull(authSecret);
        ArgumentNullException.ThrowIfNull(serverKey);
        if (userAgentPublicKey.Length != PublicKeyLength || userAgentPublicKey[0] != 0x04)
            throw new ArgumentException("p256dh must be an uncompressed P-256 point (65 bytes).", nameof(userAgentPublicKey));
        if (authSecret.Length != 16)
            throw new ArgumentException("auth must be 16 bytes.", nameof(authSecret));
        if (salt is not { Length: SaltLength })
            throw new ArgumentException("salt must be 16 bytes.", nameof(salt));
        if (plaintext.Length > MaxPlaintextLength)
            throw new ArgumentException($"Payload is {plaintext.Length} bytes; at most {MaxPlaintextLength} fit.", nameof(plaintext));

        var serverPublicKey = ExportPublicKey(serverKey);
        using var userAgent = ImportPublicKey(userAgentPublicKey);
        var ecdhSecret = LeftPad(serverKey.DeriveRawSecretAgreement(userAgent.PublicKey), KeyLength);

        // RFC 8291 §3.4: PRK_key = HMAC(auth_secret, ecdh_secret); IKM = HKDF-Expand(PRK_key, key_info, 32).
        var keyInfo = Concat(KeyInfoPrefix, userAgentPublicKey, serverPublicKey);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, KeyLength, authSecret, keyInfo);

        // RFC 8188 §2.2/2.3: PRK = HMAC(salt, IKM); CEK and NONCE are expanded from it.
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, CekInfo);
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, NonceInfo);

        // The only (so the last) record: the plaintext, then the 0x02 delimiter, no further padding.
        var record = new byte[plaintext.Length + 1];
        plaintext.CopyTo(record);
        record[^1] = 0x02;

        var body = new byte[HeaderLength + record.Length + TagLength];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(SaltLength, 4), RecordSize);
        body[SaltLength + 4] = PublicKeyLength;
        serverPublicKey.CopyTo(body, SaltLength + 5);

        using var aes = new AesGcm(cek, TagLength);
        aes.Encrypt(nonce, record,
            body.AsSpan(HeaderLength, record.Length),
            body.AsSpan(HeaderLength + record.Length, TagLength));
        return body;
    }

    /// <summary>The uncompressed public point of <paramref name="key"/>: 0x04 || X || Y, each coordinate 32 bytes.</summary>
    public static byte[] ExportPublicKey(ECDiffieHellman key) => Uncompressed(key.ExportParameters(false).Q);

    /// <summary>0x04 || X || Y with each coordinate left-padded to 32 bytes.</summary>
    public static byte[] Uncompressed(ECPoint q)
    {
        var point = new byte[PublicKeyLength];
        point[0] = 0x04;
        LeftPad(q.X!, KeyLength).CopyTo(point, 1);
        LeftPad(q.Y!, KeyLength).CopyTo(point, 1 + KeyLength);
        return point;
    }

    /// <summary>An ECDH public key from a 65-byte uncompressed point. Throws if the point is not on the curve.</summary>
    public static ECDiffieHellman ImportPublicKey(byte[] uncompressed)
    {
        if (uncompressed.Length != PublicKeyLength || uncompressed[0] != 0x04)
            throw new ArgumentException("Expected an uncompressed P-256 point (65 bytes).", nameof(uncompressed));
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uncompressed[1..(1 + KeyLength)], Y = uncompressed[(1 + KeyLength)..] },
        });
    }

    /// <summary>A big-endian value as exactly <paramref name="length"/> bytes: zeros on the left if it came out
    /// short (the bug this class exists to avoid), the leading zero bytes dropped if it came out long.</summary>
    public static byte[] LeftPad(byte[] value, int length)
    {
        if (value.Length == length) return value;
        if (value.Length > length)
        {
            var extra = value.Length - length;
            for (var i = 0; i < extra; i++)
                if (value[i] != 0) throw new CryptographicException($"Value is longer than {length} bytes.");
            return value[extra..];
        }
        var padded = new byte[length];
        value.CopyTo(padded, length - value.Length);
        return padded;
    }

    /// <summary>Base64url without padding (RFC 4648 §5), as push subscriptions and VAPID keys are written.</summary>
    public static string Base64UrlEncode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes base64url — and plain base64, with or without padding, which some stored keys use.</summary>
    public static byte[] Base64UrlDecode(string value)
    {
        var s = value.Trim().Replace('-', '+').Replace('_', '/');
        s = s.TrimEnd('=');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException("Invalid base64url length.") };
        return Convert.FromBase64String(s);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var p in parts)
        {
            p.CopyTo(result, offset);
            offset += p.Length;
        }
        return result;
    }
}
