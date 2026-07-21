using System.Text.Json;
using Microsoft.Extensions.Logging;
using WebPush;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Web Push over VAPID (the <c>WebPush</c> library). The payload is the JSON the service worker's
/// 'push' handler reads (see frontend/public/sw.js). Failures never propagate — a notification is
/// never worth breaking a request or a background sweep over.
/// </summary>
public sealed class WebPushSender : IPushSender
{
    private readonly PushOptions _options;
    private readonly ILogger<WebPushSender> _logger;
    private readonly WebPushClient _client = new();

    public WebPushSender(PushOptions options, ILogger<WebPushSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<bool> SendAsync(
        string endpoint, string p256dh, string auth, string title, string body, string? url, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return true;   // push not set up — nothing to do, and nothing to prune

        var payload = JsonSerializer.Serialize(new { title, body, url });
        var subscription = new WebPush.PushSubscription(endpoint, p256dh, auth);
        var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);

        try
        {
            await _client.SendNotificationAsync(subscription, payload, vapid, ct);
            return true;
        }
        catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            // The browser dropped the subscription (uninstalled, cleared storage) — tell the caller to prune.
            _logger.LogInformation("Push: subscription gone ({Status}), pruning", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Push: send failed");
            return true;   // transient — keep the subscription, try again next time
        }
    }
}
