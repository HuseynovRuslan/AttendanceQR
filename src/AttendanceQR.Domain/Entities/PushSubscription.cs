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

    /// <summary>The value <c>-1</c>: registered before subscriptions were bound to a session.</summary>
    public const int Unbound = -1;

    /// <summary>
    /// The <see cref="Employee.TokenVersion"/> of the session that registered this device. A push goes
    /// only to rows whose version still equals the employee's: a PIN reset, «hər yerdən çıxış» or any other
    /// TokenVersion bump ends the session on every device, and the device's pushes end with it — until the
    /// person signs in there again and the app re-registers (see PushController.Subscribe). Decided at send
    /// time (PushSessionBinding), so no TokenVersion bump anywhere has to remember to clean up after itself.
    /// <see cref="Unbound"/> marks a row from before this rule; it is adopted to the current version.
    /// </summary>
    public int TokenVersion { get; set; } = Unbound;

    public DateTime CreatedAtUtc { get; set; }
}
