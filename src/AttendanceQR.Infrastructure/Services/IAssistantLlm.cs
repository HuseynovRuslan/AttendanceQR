using System.Text.Json;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>One message in the assistant conversation, in the LLM protocol's terms.</summary>
/// <param name="Role">"system" | "user" | "assistant" | "tool".</param>
/// <param name="Content">Text content; null on an assistant turn that only calls tools.</param>
/// <param name="ToolCallId">Set on a "tool" message — which call this result answers.</param>
/// <param name="ToolCalls">Set on an assistant turn that requested tools (echoed back verbatim).</param>
public sealed record LlmMessage(string Role, string? Content, string? ToolCallId = null, JsonElement? ToolCalls = null);

/// <summary>A tool the model may call: a name, a one-line description, and a JSON-schema for args.</summary>
public sealed record LlmTool(string Name, string Description, object Parameters);

/// <summary>One requested tool call from the model.</summary>
public sealed record LlmToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// The model's turn: either final text, or a set of tool calls to execute and feed back.
/// <paramref name="RawToolCalls"/> carries the provider's original JSON so the follow-up request can
/// echo the assistant turn exactly as the protocol demands.
/// </summary>
public sealed record LlmTurn(string? Content, IReadOnlyList<LlmToolCall> ToolCalls, JsonElement? RawToolCalls);

/// <summary>
/// The LLM behind the support assistant, reduced to the one call the controller needs. An interface
/// for two reasons: tests stub it (no network, no key), and the vendor stays a configuration detail —
/// today an OpenAI-compatible endpoint, tomorrow whatever answers the same shape.
/// </summary>
public interface IAssistantLlm
{
    Task<LlmTurn> CompleteAsync(IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmTool> tools, CancellationToken ct = default);

    /// <summary>Speech → text for the chat's mic button. Language is auto-detected (az/ru here).</summary>
    Task<string> TranscribeAsync(byte[] audio, string contentType, CancellationToken ct = default);
}
