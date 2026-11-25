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
    public string[] HiddenEmailList() => ParseEmails(HiddenEmails);

    /// <summary>
    /// Comma-separated emails, out of <see cref="HiddenEmails"/>, of accounts that are still COUNTED in attendance:
    /// the today board, the tabel, hours, payroll, the dashboard, the management report and the nightly day
    /// summaries. They stay out of the lists — the «İşçilər» roster, «Giriş və rollar», the operator's paper
    /// roster — exactly as before.
    ///
    /// For a real person who clocks in at a branch, whose account must still not sit in other admins' lists: a
    /// company account an operator also signs into the console with. Listed, it would be one click away from an
    /// admin's edit form. Hidden from reports, the managers of the branch they work at cannot see that they came.
    ///
    /// An address here that is not also in HiddenEmails changes nothing — that account is counted already.
    /// Empty = every hidden account is left out of reports too, as it always was.
    /// </summary>
    public string HiddenEmailsCountedInReports { get; set; } = string.Empty;

    /// <summary>
    /// The accounts the reports leave out: <see cref="HiddenEmails"/> minus <see cref="HiddenEmailsCountedInReports"/>.
    /// One answer for the live reports (ReportQueryService) and the nightly summaries (DailySummaryService), so a
    /// day never counts somebody on one side of midnight and not on the other.
    /// </summary>
    public string[] ReportHiddenEmailList()
    {
        var counted = ParseEmails(HiddenEmailsCountedInReports).ToHashSet();
        return HiddenEmailList().Where(e => !counted.Contains(e)).ToArray();
    }

    private static string[] ParseEmails(string csv) => csv
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

    /// <summary>Employee ids that may see the shared "Tapşırıqlar" board — a config allowlist, not a
    /// role. Include a person's id in EVERY tenant they log into (login is per-tenant, so the operator
    /// who is admin in one company and manager in another has a different id in each).</summary>
    public string TaskBoardEmployeeIds { get; set; } = string.Empty;

    public Guid[] TaskBoardIdList() => TaskBoardEmployeeIds
        .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
        .Select(x => Guid.TryParse(x, out var id) ? id : (Guid?)null)
        .Where(x => x.HasValue)
        .Select(x => x!.Value)
        .ToArray();
}
