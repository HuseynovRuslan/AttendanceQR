namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// The employee support assistant ("AI Köməkçi") — an LLM-backed chat that diagnoses the problems
/// people otherwise phone the admin about (a rejected scan, a changed phone, a day that reads 0
/// hours). Bound from the "Assistant" section; the API key arrives via .env AND the compose
/// environment block — .env alone reaches nothing, see CLAUDE.md.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>SECRET — the LLM provider's API key. Empty = the assistant is off everywhere and the
    /// endpoint answers 503; nothing else in the app depends on it.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model id. The cheap "mini" tier is deliberate: the model never computes a number —
    /// every figure comes from a server-side tool — so its whole job is fluent Azerbaijani and
    /// picking the right tool, which a mini does fine.</summary>
    public string Model { get; set; } = "gpt-5.4-mini";

    /// <summary>OpenAI-compatible chat-completions base URL. A config value so the vendor is a swap,
    /// not a rewrite — any provider speaking the same protocol slots in here.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>Messages one employee may send per (Baku) day. The same idea as the face-check
    /// budget: an authenticated caller must not be an open tap on a metered API.</summary>
    public int DailyMessageLimit { get; set; } = 30;

    /// <summary>How many tool round-trips a single reply may take before the model must answer with
    /// what it has. A loop guard, not a feature.</summary>
    public int MaxToolRounds { get; set; } = 4;

    /// <summary>Speech-to-text model for the mic button. Auto-detects the language — the workforce
    /// speaks Azerbaijani and Russian, and a language picker would be one more thing to explain.</summary>
    public string TranscribeModel { get; set; } = "gpt-4o-mini-transcribe";

    /// <summary>Voice clips one employee may transcribe per day — its own budget, so a voice question
    /// (transcribe + chat) doesn't double-charge the message budget.</summary>
    public int DailyTranscribeLimit { get; set; } = 40;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
