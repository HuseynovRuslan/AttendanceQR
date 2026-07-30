namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One Web Push subscription — a single browser/PWA install that agreed to receive notifications.
/// An employee may hold several (phone + spare), the same way they hold several device bindings.
/// The endpoint is the push service's URL and is globally unique, so it doubles as the natural key.
/// Tenant-scoped.
/// </summary>
public class PushSubscription : ITenantScoped
{
    public PushSubscription()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The push service endpoint this browser was issued. Unique.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Client public key (base64url) from the PushSubscription keys.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Client auth secret (base64url) from the PushSubscription keys.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>FCM device token when this row is a NATIVE app registration (Capacitor Android/iOS)
    /// rather than a Web Push subscription. When set, the Web Push keys are empty and Endpoint holds
    /// "fcm:{token}" (to keep the unique Endpoint index happy); the notifier sends via FCM instead.</summary>
    public string? FcmToken { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
