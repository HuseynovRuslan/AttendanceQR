namespace AttendanceQR.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing key; must be at least 256 bits (32 bytes).</summary>
    public string SigningKey { get; set; } = string.Empty;

    // Effectively never expires (~100 years) — log in once, stay in, by explicit request. A JWT
    // needs *some* exp claim to stay a well-formed, standard token, so this is a very-far-future
    // date rather than omitting expiry. There is no revocation/refresh mechanism, so a leaked
    // token stays valid indefinitely; that trade-off is accepted here.
    public int ExpiryMinutes { get; set; } = 52_560_000;
}
