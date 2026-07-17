using System.Linq;

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

    /// <summary>Parsed, lowercased list of hidden emails (from the comma-separated HiddenEmails).</summary>
    public string[] HiddenEmailList() => HiddenEmails
        .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
        .Select(x => x.ToLowerInvariant())
        .ToArray();

    /// <summary>
    /// Comma-separated Employee IDs allowed to manage TENANTS — create a company, disable one, see
    /// across all of them. Empty = nobody, and the super-admin screen simply does not exist.
    ///
    /// IDs, not emails, on purpose. Emails are unique per tenant, not globally — `(TenantId, Email)`
    /// is the index — so any tenant's own admin could set their address to the operator's and, on
    /// their next login, carry a token that an email-based check would accept. An employee id cannot
    /// be taken by someone else.
    /// </summary>
    public string SuperAdminEmployeeIds { get; set; } = string.Empty;

    /// <summary>Parsed super-admin employee ids; unparseable entries are ignored rather than
    /// widening the check.</summary>
    public Guid[] SuperAdminIdList() => SuperAdminEmployeeIds
        .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
        .Select(x => Guid.TryParse(x, out var id) ? id : (Guid?)null)
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();
}
