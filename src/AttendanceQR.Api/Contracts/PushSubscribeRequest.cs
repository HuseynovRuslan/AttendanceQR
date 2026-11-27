namespace AttendanceQR.Api.Contracts;

/// <summary>A browser's Web Push subscription, as produced by PushManager.subscribe().
/// <paramref name="Refresh"/>: the app re-registering a subscription it already had, after a sign-in —
/// not the person tapping «aktiv et». A refresh never takes over an endpoint another account holds (one
/// browser, several accounts on a crew phone); an explicit enable does, as it always has.</summary>
public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth, bool Refresh = false);

public record PushUnsubscribeRequest(string Endpoint);

/// <summary>A native app's FCM device token, from @capacitor/push-notifications registration.</summary>
public record PushRegisterNativeRequest(string Token);
