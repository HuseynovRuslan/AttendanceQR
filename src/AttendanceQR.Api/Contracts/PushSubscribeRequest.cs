namespace AttendanceQR.Api.Contracts;

/// <summary>A browser's Web Push subscription, as produced by PushManager.subscribe().</summary>
public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);

public record PushUnsubscribeRequest(string Endpoint);
