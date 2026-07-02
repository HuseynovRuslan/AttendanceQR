namespace AttendanceQR.Infrastructure.Security;

public class QrTokenOptions
{
    public const string SectionName = "QrToken";

    /// <summary>HMAC signing secret. Bound from configuration "QrToken:Secret".</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Token lifetime in seconds. Bound from "QrToken:TtlSeconds" (default 60).</summary>
    public int TtlSeconds { get; set; } = 60;
}
