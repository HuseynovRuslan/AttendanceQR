using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// <see cref="IAssistantLlm"/> over the OpenAI-compatible chat-completions protocol. Hand-rolled JSON
/// rather than a vendor SDK on purpose: the surface we use is one POST, and a NuGet SDK would pin us
/// to one provider's package cadence for a protocol several providers speak identically.
/// </summary>
public sealed class OpenAiAssistantLlm : IAssistantLlm
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly AssistantOptions _options;

    public OpenAiAssistantLlm(HttpClient http, AssistantOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<LlmTurn> CompleteAsync(
        IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, CancellationToken ct = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = messages.Select(ToWire).ToList(),
            // Hard cap on the reply. Support answers are a few sentences; anything longer is the
            // model rambling on a metered API.
            ["max_completion_tokens"] = 700,
        };
        if (tools.Count > 0)
        {
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new { name = t.Name, description = t.Description, parameters = t.Parameters },
            }).ToList();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"assistant LLM: {(int)response.StatusCode} {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;

        var calls = new List<LlmToolCall>();
        JsonElement? rawCalls = null;
        if (message.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array)
        {
            // Clone: the JsonDocument is disposed with this scope, the echo must outlive it.
            rawCalls = tc.Clone();
            foreach (var call in tc.EnumerateArray())
            {
                calls.Add(new LlmToolCall(
                    call.GetProperty("id").GetString() ?? string.Empty,
                    call.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                    call.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"));
            }
        }

        return new LlmTurn(content, calls, rawCalls);
    }

    public async Task<string> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default)
    {
        // The transcription endpoint is multipart, not JSON — the one place the protocol differs.
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_options.TranscribeModel), "model");
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        // The extension matters: the service sniffs the container from the NAME, and MediaRecorder
        // produces webm on Android/Chrome but mp4 on iOS Safari.
        var ext = contentType.Contains("mp4") ? "mp4" : contentType.Contains("mpeg") ? "mp3" : "webm";
        form.Add(file, "file", $"voice.{ext}");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = form;

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"assistant transcribe: {(int)response.StatusCode} {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
    }

    private static object ToWire(LlmMessage m)
    {
        // The protocol's shape differs per role: an assistant turn echoes its tool_calls, a tool turn
        // names the call it answers. Everything else is role+content.
        if (m.Role == "assistant" && m.ToolCalls is JsonElement calls)
            return new { role = "assistant", content = m.Content, tool_calls = calls };
        if (m.Role == "tool")
            return new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content ?? string.Empty };
        return new { role = m.Role, content = m.Content ?? string.Empty };
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
