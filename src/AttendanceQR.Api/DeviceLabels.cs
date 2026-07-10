namespace AttendanceQR.Api;

/// <summary>
/// Best-effort device name from the User-Agent, for bindings created server-side (auto-bind), where
/// the client never sends the label it captures at activation. Cosmetic — shown in the admin list,
/// never used for any security decision. Mirrors getFriendlyDeviceName() in the frontend.
/// </summary>
public static class DeviceLabels
{
    public static string FromUserAgent(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "Naməlum cihaz";
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "iPhone";
        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iPad";
        if (ua.Contains("Samsung", StringComparison.OrdinalIgnoreCase)) return "Samsung Galaxy";
        if (ua.Contains("Xiaomi", StringComparison.OrdinalIgnoreCase) || ua.Contains("MIUI", StringComparison.OrdinalIgnoreCase)) return "Xiaomi";
        if (ua.Contains("Huawei", StringComparison.OrdinalIgnoreCase)) return "Huawei";
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android cihaz";
        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows PC";
        if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "Mac";
        return "Naməlum cihaz";
    }
}
