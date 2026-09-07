namespace AttendanceQR.Infrastructure.Security;

public class QrTokenOptions
{
    public const string SectionName = "QrToken";

    /// <summary>HMAC signing secret. Bound from configuration "QrToken:Secret".</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Token lifetime in seconds. Bound from "QrToken:TtlSeconds" (default 60).</summary>
    public int TtlSeconds { get; set; } = 60;

    /// <summary>
    /// Optional SHA-256 fingerprint (64 hexadecimal characters) of one printed QR that may remain
    /// valid after its embedded expiry. The raw token never belongs in configuration or logs.
    /// Signature, location version, tenant, geofence, device and photo checks still apply.
    /// </summary>
    public string ExpiryExemptTokenSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Location the fingerprinted legacy poster must name. Both exemption values are required; an
    /// incomplete configuration grants no exemption and is rejected during API startup.
    /// </summary>
    public Guid? ExpiryExemptLocationId { get; set; }
}
