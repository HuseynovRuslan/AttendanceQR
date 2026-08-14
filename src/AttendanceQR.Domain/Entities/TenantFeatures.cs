namespace AttendanceQR.Domain.Entities;

/// <summary>
/// The catalogue of per-tenant feature flags and the single place that reads
/// <see cref="Tenant.DisabledFeatures"/>. Flags are opt-OUT: a feature is ON unless its key appears in
/// the tenant's disabled list, so a new feature ships enabled everywhere with no data change. Keys are
/// stable strings — never rename one, existing disabled-lists reference it.
///
/// Only features that are actually ENFORCED live here. A toggle in the super-admin panel that changed
/// nothing would be a trap: the operator would think they turned a feature off while it kept running.
/// Each key below has a real gate — a menu item that hides and a <c>[RequireFeature]</c> guard that
/// 403s the endpoint. The list grows one entry at a time, as each feature earns its on/off switch.
/// </summary>
public static class TenantFeatures
{
    // Stable keys (never rename). The order here is the order shown in the super-admin toggle list.
    public const string Payroll = "payroll";
    public const string Announcements = "announcements";
    public const string Assistant = "assistant";

    /// <summary>Every togglable feature, with a human (Azerbaijani) label for the super-admin UI.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Payroll, "Maaş hesabatı"),
        (Announcements, "Elanlar"),
        (Assistant, "AI Köməkçi"),
    };

    /// <summary>Parses the opt-out CSV into a set of disabled keys (lowercased).</summary>
    public static HashSet<string> ParseDisabled(string? disabledCsv) =>
        (disabledCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToHashSet();

    /// <summary>Is <paramref name="key"/> enabled for a tenant whose disabled list is <paramref name="disabledCsv"/>?</summary>
    public static bool IsEnabled(string? disabledCsv, string key) =>
        !ParseDisabled(disabledCsv).Contains(key.ToLowerInvariant());

    /// <summary>Normalises disabled keys to a clean CSV (known keys only, deduped), or null when nothing
    /// is disabled — so an all-on tenant stores null rather than an empty string.</summary>
    public static string? ToCsv(IEnumerable<string> disabledKeys)
    {
        var known = All.Select(f => f.Key).ToHashSet();
        var clean = disabledKeys.Select(k => k.ToLowerInvariant()).Where(known.Contains).Distinct().ToList();
        return clean.Count == 0 ? null : string.Join(",", clean);
    }
}
