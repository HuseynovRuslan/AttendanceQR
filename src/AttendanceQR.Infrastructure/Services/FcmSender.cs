using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>Sends a push to one NATIVE app device (Capacitor Android/iOS) via FCM. The mirror of
/// <see cref="IPushSender"/> for Web Push: never throws, and reports a dead token so the caller
/// prunes it — the same "return false = delete the row" contract.</summary>
public interface IFcmSender
{
    bool IsConfigured { get; }

    /// <summary>Returns false when the token is gone (unregistered/invalid) and should be deleted.</summary>
    Task<bool> SendAsync(string token, string title, string body, string? url, CancellationToken ct = default);
}

public sealed class FirebaseFcmSender : IFcmSender
{
    private readonly FirebaseMessaging? _messaging;
    private readonly ILogger<FirebaseFcmSender> _logger;

    public bool IsConfigured => _messaging is not null;

    public FirebaseFcmSender(FcmOptions options, ILogger<FirebaseFcmSender> logger)
    {
        _logger = logger;
        if (!options.IsConfigured)
        {
            _logger.LogInformation("FCM not configured — native app push is off (Web Push still works).");
            return;
        }
        try
        {
            // A NAMED app ("qrlog") so we never clash with a default FirebaseApp; created once because
            // this sender is a singleton. GetInstance for a missing app returns null (not throws) in
            // this SDK version, so null-coalesce into Create — and still catch, in case a version does
            // throw instead.
            FirebaseApp? app;
            try { app = FirebaseApp.GetInstance("qrlog"); }
            catch { app = null; }
            app ??= FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromJson(options.ServiceAccountJson),
                ProjectId = options.ProjectId,
            }, "qrlog");
            _messaging = FirebaseMessaging.GetMessaging(app);
            _logger.LogInformation("FCM ready (project {ProjectId}).", options.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM init failed — native app push disabled.");
        }
    }

    public async Task<bool> SendAsync(string token, string title, string body, string? url, CancellationToken ct = default)
    {
        if (_messaging is null) return true; // not configured — no-op, keep the row

        var message = new Message
        {
            Token = token,
            Notification = new Notification { Title = title, Body = body },
            Data = url is null ? null : new Dictionary<string, string> { ["url"] = url },
            Android = new AndroidConfig { Priority = Priority.High },
        };
        try
        {
            await _messaging.SendAsync(message, ct);
            return true;
        }
        catch (FirebaseMessagingException ex) when (
            ex.MessagingErrorCode is MessagingErrorCode.Unregistered
                or MessagingErrorCode.InvalidArgument
                or MessagingErrorCode.SenderIdMismatch)
        {
            return false; // token dead / malformed / wrong project → prune it
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FCM send failed (transient) — keeping token.");
            return true;
        }
    }
}
