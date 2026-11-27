using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Web Push: the payload encrypted per RFC 8291 (<c>aes128gcm</c>, <see cref="WebPushCrypto"/>), the request signed
/// per RFC 8292 (VAPID, <see cref="VapidSigner"/>), POSTed to the subscription's push service. The payload is the
/// JSON the service worker's 'push' handler reads (see frontend/public/sw.js).
///
/// A failed send never throws: the <see cref="PushSendResult"/> says whether the subscription is gone (prune it),
/// whether a later attempt may succeed (Transient), or whether the push service refused it outright (Rejected).
/// Only cancellation of the caller's token propagates, so the caller's per-send timeout stays its own decision.
/// </summary>
public sealed class WebPushSender : IPushSender, IDisposable
{
    /// <summary>How long the push service keeps an undelivered message (seconds). Four weeks — the default of the
    /// library this replaced, kept so a phone that was off for a while still gets what it missed.</summary>
    public const int TimeToLiveSeconds = 2_419_200;

    private readonly PushOptions _options;
    private readonly ILogger<WebPushSender> _logger;
    private readonly HttpClient _http;
    private readonly VapidSigner? _vapid;

    public WebPushSender(PushOptions options, ILogger<WebPushSender> logger)
        : this(options, logger, new SocketsHttpHandler
        {
            // One long-lived client; connections are recycled so a push service's DNS change is picked up.
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        }, TimeProvider.System)
    {
    }

    /// <summary>For tests: a fake handler instead of the network, a fixed clock for the VAPID tokens.</summary>
    public WebPushSender(PushOptions options, ILogger<WebPushSender> logger, HttpMessageHandler handler, TimeProvider clock)
    {
        _options = options;
        _logger = logger;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        if (options.IsConfigured)
        {
            try
            {
                _vapid = new VapidSigner(options.PublicKey, options.PrivateKey, options.Subject, clock);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
            {
                // Bad keys must not take the app down; every send then reports Rejected and says why in the log.
                _logger.LogError(ex, "Push: the VAPID keys could not be read — web push will be refused until they are fixed");
            }
        }
    }

    public async Task<PushSendResult> SendAsync(
        string endpoint, string p256dh, string auth, string title, string body, string? url, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return PushSendResult.NotConfigured;   // push not set up — nothing to do, and nothing to prune
        if (_vapid is null)
            return PushSendResult.Rejected;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            _logger.LogWarning("Push: subscription endpoint is not an http(s) URL — not sent");
            return PushSendResult.Rejected;
        }

        byte[] content;
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { title, body, url });
            content = WebPushCrypto.Encrypt(payload, WebPushCrypto.Base64UrlDecode(p256dh), WebPushCrypto.Base64UrlDecode(auth));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            // A subscription whose keys do not parse (or a payload too large) — retrying the same thing cannot help.
            _logger.LogWarning(ex, "Push: could not encrypt for {Host} — not sent", uri.Host);
            return PushSendResult.Rejected;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", TimeToLiveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Authorization", _vapid.AuthorizationFor(uri));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // the caller's own deadline — theirs to count
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // DNS, refused connection, reset, or the client's own timeout: the push service may be fine in a minute.
            _logger.LogWarning(ex, "Push: could not reach {Host}", uri.Host);
            return PushSendResult.Transient;
        }

        using (response)
        {
            var result = Classify(response.StatusCode);
            switch (result)
            {
                case PushSendResult.Gone:
                    // The browser dropped the subscription (uninstalled, cleared storage) — tell the caller to prune.
                    _logger.LogInformation("Push: subscription gone ({Status}), pruning", (int)response.StatusCode);
                    break;
                case PushSendResult.Transient:
                    _logger.LogWarning("Push: {Host} answered {Status} — will try again later", uri.Host, (int)response.StatusCode);
                    break;
                case PushSendResult.Rejected:
                    _logger.LogWarning("Push: {Host} refused the push ({Status}): {Reason}",
                        uri.Host, (int)response.StatusCode, await ReasonAsync(response, ct));
                    break;
            }
            return result;
        }
    }

    /// <summary>A push service's answer as an outcome. 2xx delivered; 404/410 the subscription is gone; 408, 429 and
    /// 5xx may pass later; anything else (400, 401, 403, 413 …) is a refusal a retry will not change.</summary>
    public static PushSendResult Classify(HttpStatusCode status)
    {
        var code = (int)status;
        if (code is >= 200 and < 300) return PushSendResult.Delivered;
        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone) return PushSendResult.Gone;
        if (status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || code >= 500) return PushSendResult.Transient;
        return PushSendResult.Rejected;
    }

    /// <summary>The first part of the push service's explanation, for the log. Never the request: that carries the token.</summary>
    private static async Task<string> ReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            text = text.ReplaceLineEndings(" ").Trim();
            return text.Length > 200 ? text[..200] + "…" : text;
        }
        catch (Exception)
        {
            return "(no body)";
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _vapid?.Dispose();
    }
}
