namespace AttendanceQR.Infrastructure.Services;

/// <summary>What became of one push to one device (a browser subscription or a native app token).</summary>
public enum PushSendResult
{
    /// <summary>The push service accepted it (2xx). It is on its way to the device.</summary>
    Delivered,

    /// <summary>The device is gone for good (Web Push 404/410, FCM unregistered token): delete the row.</summary>
    Gone,

    /// <summary>Could not be handed over this time — 429, 5xx, a network error, a timeout. The row stays and a
    /// later attempt may well succeed. Never counted as delivered.</summary>
    Transient,

    /// <summary>The push service refused it for a reason a retry will not fix (400, 401, 403, 413 …) — usually our
    /// configuration or payload, not the device. The row stays (it may be fine); logged as a warning.</summary>
    Rejected,

    /// <summary>This channel is not set up on this server (no VAPID keys / no FCM project): nothing was attempted.</summary>
    NotConfigured,
}

/// <summary>Sends a Web Push notification to one subscription. Never throws for a failed send — the outcome says
/// what happened. Cancellation of <c>ct</c> does propagate, so the caller can tell its own timeout apart.</summary>
public interface IPushSender
{
    Task<PushSendResult> SendAsync(
        string endpoint, string p256dh, string auth, string title, string body, string? url, CancellationToken ct = default);
}
