using AttendanceQR.Domain.Notifications;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One push owed to one person — the delivery status the center's notification does not carry: pending, sent, no
/// device, failed after retries, or folded into a summary push.
///
/// Push goes through the existing <c>IPushNotifier</c> (Web Push and FCM, with its own pruning of dead
/// subscriptions), so a phone is still a <see cref="PushSubscription"/>: there is no second token table. The live web
/// channel (SignalR) is not tracked here — it reaches whoever has the panel open at that moment, and the center is
/// where a missed one is found.
/// </summary>
public class NotificationDelivery : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>The center line it belongs to; null when the person keeps the center off and wants the push alone.</summary>
    public Guid? NotificationId { get; set; }

    public Guid RecipientEmployeeId { get; set; }

    public NotificationChannel Channel { get; set; } = NotificationChannel.Push;

    /// <summary>The type it is about — what a summary push counts.</summary>
    public string TypeKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Url { get; set; }

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
}
