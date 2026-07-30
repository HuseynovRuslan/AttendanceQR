namespace AttendanceQR.Infrastructure.Services;

/// <summary>Firebase Cloud Messaging settings for NATIVE app push — the Capacitor Android/iOS app,
/// whose WebView cannot do Web Push. Bound from the "Fcm" section. Empty = FCM off and every native
/// send is a silent no-op, so the server runs fine unconfigured (Web Push still serves browsers/PWAs).</summary>
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>Firebase project id, e.g. "qrlog-4b081".</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>The service-account JSON (whole file). SECRET — server-side only. Usually supplied
    /// base64-encoded in the environment (FCM_SERVICE_ACCOUNT_B64) and decoded once at startup.</summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(ServiceAccountJson);
}
