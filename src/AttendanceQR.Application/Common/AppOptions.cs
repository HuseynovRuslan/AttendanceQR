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
}
