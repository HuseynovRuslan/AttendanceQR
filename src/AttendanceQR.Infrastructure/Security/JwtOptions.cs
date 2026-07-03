namespace AttendanceQR.Infrastructure.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing key; must be at least 256 bits (32 bytes).</summary>
    public string SigningKey { get; set; } = string.Empty;

    // 30 days — employees scan in/out from their own phone daily and shouldn't have to re-login
    // constantly. There is no revocation/refresh mechanism, so a leaked token stays valid for the
    // full period; that trade-off is accepted here in exchange for "log in once, stay in".
    public int ExpiryMinutes { get; set; } = 43_200;
}
