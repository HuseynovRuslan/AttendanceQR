namespace AttendanceQR.Infrastructure.Services;

/// <summary>Sends a Web Push notification to one subscription. Never throws: a dead/expired
/// subscription is reported so the caller can prune it, anything else is swallowed and logged.</summary>
public interface IPushSender
{
    /// <summary>Returns false when the subscription is gone (404/410) and should be deleted.</summary>
    Task<bool> SendAsync(string endpoint, string p256dh, string auth, string title, string body, string? url, CancellationToken ct = default);
}
