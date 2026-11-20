namespace AttendanceQR.Application.Common;

/// <summary>
/// The daily management report on Telegram — bound from the "DailyReport" section
/// (env: DailyReport__TelegramBotToken, …; every one must also be listed in the compose files'
/// <c>environment:</c>, a value only in .env reaches nothing). With no bot token the feature is inert:
/// nothing is sent, the bot listens to nobody, and the admin screen says so.
/// </summary>
public sealed class DailyReportOptions
{
    public const string SectionName = "DailyReport";

    /// <summary>The bot's token from @BotFather. A secret: never logged, never in a URL that is logged.</summary>
    public string TelegramBotToken { get; set; } = string.Empty;

    /// <summary>The bot's @username without the @ — used to build the «connect» link (t.me/&lt;name&gt;?start=…).</summary>
    public string TelegramBotUsername { get; set; } = string.Empty;

    /// <summary>
    /// The API's public address. The report link is &lt;this&gt;/api/public/daily-report/&lt;token&gt; — on the
    /// API host because the company subdomains serve only the frontend. The token names its company, and
    /// the page resolves that company before it looks anything up, so a link opens one company's data only.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "https://api.qrlog.az";

    /// <summary>The headless browser that prints the PDF (installed in the backend image).</summary>
    public string ChromePath { get; set; } = "google-chrome";

    /// <summary>How long the «open the report» link keeps working.</summary>
    public int LinkValidDays { get; set; } = 7;

    /// <summary>Sending gives up after this many failed attempts (with growing waits between them).</summary>
    public int MaxAttempts { get; set; } = 6;

    public bool TelegramConfigured => !string.IsNullOrWhiteSpace(TelegramBotToken);
}
