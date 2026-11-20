using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AttendanceQR.Application.Common;

namespace AttendanceQR.Api.DailyReport;

/// <summary>The outcome of one Telegram call. <see cref="Permanent"/> = retrying cannot help (chat gone, bot blocked).</summary>
public sealed record TelegramResult(bool Ok, string? Error = null, bool Permanent = false, int? RetryAfterSeconds = null);

public sealed record TelegramUpdate(long UpdateId, long? ChatId, string? Text);

public interface ITelegramClient
{
    Task<TelegramResult> SendDocumentAsync(long chatId, byte[] file, string fileName, string captionHtml,
        string buttonText, string buttonUrl, CancellationToken ct);
    Task<TelegramResult> SendMessageAsync(long chatId, string textHtml, CancellationToken ct);
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct);
}

/// <summary>
/// Telegram Bot API over the named HttpClient "telegram" — whose logging is switched OFF in Program.cs,
/// because the bot token is part of every request URL and the default HttpClient logs URLs. Every error
/// leaving this class has the token scrubbed out of it as well.
/// </summary>
public sealed class TelegramClient : ITelegramClient
{
    private readonly IHttpClientFactory _http;
    private readonly DailyReportOptions _options;

    public TelegramClient(IHttpClientFactory http, DailyReportOptions options)
    {
        _http = http;
        _options = options;
    }

    private string Url(string method) => $"https://api.telegram.org/bot{_options.TelegramBotToken}/{method}";

    public async Task<TelegramResult> SendDocumentAsync(long chatId, byte[] file, string fileName, string captionHtml,
        string buttonText, string buttonUrl, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "chat_id");
        var doc = new ByteArrayContent(file);
        doc.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(doc, "document", fileName);
        form.Add(new StringContent(captionHtml, Encoding.UTF8), "caption");
        form.Add(new StringContent("HTML"), "parse_mode");
        var markup = JsonSerializer.Serialize(new { inline_keyboard = new[] { new[] { new { text = buttonText, url = buttonUrl } } } });
        form.Add(new StringContent(markup, Encoding.UTF8), "reply_markup");
        return await CallAsync("sendDocument", form, ct);
    }

    public async Task<TelegramResult> SendMessageAsync(long chatId, string textHtml, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { chat_id = chatId, text = textHtml, parse_mode = "HTML", disable_web_page_preview = true });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        return await CallAsync("sendMessage", content, ct);
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct)
    {
        var client = _http.CreateClient("telegram");
        var url = Url("getUpdates") + $"?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D";
        using var resp = await client.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        ApiResponse<List<Update>>? parsed;
        try { parsed = JsonSerializer.Deserialize<ApiResponse<List<Update>>>(json); }
        catch (JsonException) { return []; }
        if (parsed is not { Ok: true } || parsed.Result is null)
            return [];
        return parsed.Result.Select(u => new TelegramUpdate(u.UpdateId, u.Message?.Chat?.Id, u.Message?.Text)).ToList();
    }

    private async Task<TelegramResult> CallAsync(string method, HttpContent content, CancellationToken ct)
    {
        try
        {
            var client = _http.CreateClient("telegram");
            using var resp = await client.PostAsync(Url(method), content, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            ApiResponse<JsonElement>? parsed = null;
            try { parsed = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(json); } catch (JsonException) { }
            if (parsed is { Ok: true }) return new TelegramResult(true);

            var code = parsed?.ErrorCode ?? (int)resp.StatusCode;
            var description = Scrub(parsed?.Description ?? $"HTTP {(int)resp.StatusCode}");
            // 400 «chat not found», 403 «bot was blocked by the user» / «user is deactivated»: waiting will not fix these.
            var permanent = code is 400 or 403 or 404;
            return new TelegramResult(false, $"{code}: {description}", permanent, parsed?.Parameters?.RetryAfter);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new TelegramResult(false, Scrub(ex.GetType().Name + ": " + ex.Message));
        }
    }

    /// <summary>Removes the bot token from any text that might be stored or logged, and keeps it short.</summary>
    public string Scrub(string text)
    {
        var token = _options.TelegramBotToken;
        if (!string.IsNullOrEmpty(token)) text = text.Replace(token, "***", StringComparison.Ordinal);
        return text.Length > 300 ? text[..300] : text;
    }

    private sealed class ApiResponse<T>
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("result")] public T? Result { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("parameters")] public ResponseParameters? Parameters { get; set; }
    }

    private sealed class ResponseParameters
    {
        [JsonPropertyName("retry_after")] public int? RetryAfter { get; set; }
    }

    private sealed class Update
    {
        [JsonPropertyName("update_id")] public long UpdateId { get; set; }
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }

    private sealed class Message
    {
        [JsonPropertyName("chat")] public Chat? Chat { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class Chat
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }
}
