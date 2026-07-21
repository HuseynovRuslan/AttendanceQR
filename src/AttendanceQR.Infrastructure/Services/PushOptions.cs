namespace AttendanceQR.Infrastructure.Services;

/// <summary>Web Push (VAPID) settings, bound from the "Push" configuration section. Empty keys =
/// push is off and every send is a silent no-op, so the app runs fine without it configured.</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>VAPID public key (base64url). Handed to the browser when it subscribes.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>VAPID private key (base64url). SECRET — server-side only, never sent to a client.</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>VAPID subject: a mailto: or https: URL identifying this application server.</summary>
    public string Subject { get; set; } = "mailto:admin@qrlog.az";

    /// <summary>How many minutes BEFORE the shift ends the checkout reminder goes out. Deliberately
    /// ahead of time: a reminder sent after the shift arrives when the employee is already home and
    /// can no longer scan out.</summary>
    public int CheckoutReminderLeadMinutes { get; set; } = 10;

    /// <summary>How many minutes BEFORE the shift starts to nudge someone who has not checked in.</summary>
    public int CheckInReminderLeadMinutes { get; set; } = 10;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
