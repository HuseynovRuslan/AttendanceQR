using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>Sends a push to one NATIVE app device (Capacitor Android/iOS) via FCM. The mirror of
/// <see cref="IPushSender"/> for Web Push: never throws for a failed send, and answers with the same
/// <see cref="PushSendResult"/> — Gone means delete the row, Transient means try again later. Cancellation of
/// <c>ct</c> propagates.</summary>
public interface IFcmSender
{
    bool IsConfigured { get; }

    Task<PushSendResult> SendAsync(string token, string title, string body, string? url, CancellationToken ct = default);
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

    public async Task<PushSendResult> SendAsync(string token, string title, string body, string? url, CancellationToken ct = default)
    {
        if (_messaging is null) return PushSendResult.NotConfigured; // not configured — no-op, keep the row

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
            return PushSendResult.Delivered;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller's own deadline
        }
        catch (FirebaseMessagingException ex)
        {
            var result = Classify(ex.MessagingErrorCode, ex.ErrorCode);
            if (result == PushSendResult.Transient)
                _logger.LogWarning(ex, "FCM send failed (transient) — keeping token, will try again.");
            else if (result == PushSendResult.Rejected)
                _logger.LogWarning(ex, "FCM refused the push ({Code}) — keeping token.", ex.MessagingErrorCode?.ToString() ?? ex.ErrorCode.ToString());
            return result;
        }
        catch (Exception ex)
        {
            // Network, timeouts inside the SDK, an auth refresh that failed on the way: worth another try later.
            _logger.LogWarning(ex, "FCM send failed (transient) — keeping token.");
            return PushSendResult.Transient;
        }
    }

    /// <summary>An FCM error as an outcome: a dead or malformed token is Gone (prune — as before); an outage,
    /// internal error or quota is Transient; anything else (APNs credentials, permissions) is Rejected.</summary>
    public static PushSendResult Classify(MessagingErrorCode? messagingCode, ErrorCode errorCode) => messagingCode switch
    {
        MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument or MessagingErrorCode.SenderIdMismatch
            => PushSendResult.Gone,
        MessagingErrorCode.Unavailable or MessagingErrorCode.Internal or MessagingErrorCode.QuotaExceeded
            => PushSendResult.Transient,
        MessagingErrorCode.ThirdPartyAuthError => PushSendResult.Rejected,
        _ => errorCode switch
        {
            ErrorCode.Unavailable or ErrorCode.Internal or ErrorCode.DeadlineExceeded or ErrorCode.ResourceExhausted
                or ErrorCode.Aborted or ErrorCode.Unknown or ErrorCode.Cancelled => PushSendResult.Transient,
            _ => PushSendResult.Rejected,
        },
    };
}
