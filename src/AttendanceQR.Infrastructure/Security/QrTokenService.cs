using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// Stateless QR token codec.
/// <para>
/// Wire format: the string <c>{locationId}.{unixSeconds}.{nonce}.{signature}</c>
/// (nonce and signature are Base64Url), and the whole string is then Base64Url-encoded
/// into one opaque token. The signature is <c>HMACSHA256(secret, "{locationId}.{unixSeconds}.{nonce}")</c>.
/// </para>
/// </summary>
public sealed class QrTokenService : IQrTokenService
{
    private readonly QrTokenOptions _options;

    public QrTokenService(IOptions<QrTokenOptions> options)
    {
        _options = options.Value;
    }

    public string Generate(Guid locationId)
    {
        // Server clock is the single source of truth for the timestamp.
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

        var signingInput = $"{locationId}.{timestamp}.{nonce}";
        var signature = Base64Url.EncodeToString(ComputeSignature(signingInput));

        var payload = $"{signingInput}.{signature}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    public QrTokenValidationResult Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return QrTokenValidationResult.Fail("TokenMalformed");

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        }
        catch (FormatException)
        {
            return QrTokenValidationResult.Fail("TokenMalformed");
        }

        var parts = payload.Split('.');
        if (parts.Length != 4)
            return QrTokenValidationResult.Fail("TokenMalformed");

        if (!Guid.TryParse(parts[0], out var locationId))
            return QrTokenValidationResult.Fail("TokenMalformed");

        if (!long.TryParse(parts[1], out var timestamp))
            return QrTokenValidationResult.Fail("TokenMalformed");

        var nonce = parts[2];
        var signingInput = $"{parts[0]}.{parts[1]}.{parts[2]}";

        byte[] providedSignature;
        try
        {
            providedSignature = Base64Url.DecodeFromChars(parts[3]);
        }
        catch (FormatException)
        {
            return QrTokenValidationResult.Fail("SignatureInvalid");
        }

        var expectedSignature = ComputeSignature(signingInput);

        // Constant-time comparison to avoid signature timing oracles.
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
            return QrTokenValidationResult.Fail("SignatureInvalid");

        // Expiry is judged only against the server clock — never against any client time.
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (timestamp + _options.TtlSeconds < nowUnix)
            return QrTokenValidationResult.Fail("TokenExpired");

        return QrTokenValidationResult.Success(locationId, nonce);
    }

    private byte[] ComputeSignature(string signingInput) =>
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_options.Secret),
            Encoding.UTF8.GetBytes(signingInput));
}
