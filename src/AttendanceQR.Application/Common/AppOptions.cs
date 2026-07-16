namespace AttendanceQR.Application.Common;

/// <summary>App-wide settings bound from the "App" configuration section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// IANA time zone used to interpret local shift times against UTC timestamps.
    /// Default Asia/Baku (UTC+4). Never hard-coded in logic — read from here.
    /// </summary>
    public string TimeZone { get; set; } = "Asia/Baku";

    /// <summary>
    /// Comma-separated emails hidden from the admin "İşçilər" roster — the system/root admin accounts
    /// created by bootstrap/seed. They still exist and work fully; they're just not listed (they're
    /// operators, not staff to manage). Empty = hide nobody.
    /// </summary>
    public string HiddenEmails { get; set; } = string.Empty;
}
