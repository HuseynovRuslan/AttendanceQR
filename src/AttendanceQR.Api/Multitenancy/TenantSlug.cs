namespace AttendanceQR.Api.Multitenancy;

/// <summary>
/// Extracts the tenant slug from an anonymous request. The app frontends live at
/// <c>&lt;slug&gt;.qrlog.az</c> while the API is at <c>api.qrlog.az</c>, so for login/activate/kiosk
/// (no JWT yet) the tenant comes from the browser-sent Origin/Referer host.
/// </summary>
public static class TenantSlug
{
    private static readonly HashSet<string> NonTenantLabels =
        new(StringComparer.OrdinalIgnoreCase) { "api", "www", "localhost", "qrlog", "127" };

    public static string? FromRequest(HttpRequest request)
    {
        var host = HostFromHeader(request.Headers["Origin"].ToString())
                   ?? HostFromHeader(request.Headers["Referer"].ToString())
                   ?? request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        var label = host.Split('.')[0].ToLowerInvariant();
        if (label.Length == 0 || NonTenantLabels.Contains(label))
            return null;
        return label;
    }

    private static string? HostFromHeader(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
