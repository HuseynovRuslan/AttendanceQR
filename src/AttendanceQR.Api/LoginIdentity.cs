namespace AttendanceQR.Api;

/// <summary>
/// The key a login attempt is rate-limited under.
///
/// The lockout budget has to be spent by the ACCOUNT, not by the string someone typed. Login accepts
/// an email or a phone number and resolves the phone through <see cref="PhoneNumbers.Normalize"/> —
/// so "0501234567", "+994 50 123 45 67" and "(050) 123-45-67" are one account. Keying the lockout on
/// the raw text instead gave each spelling its own 5-attempt budget, and an attacker can spell one
/// number in unlimited ways (spaces, dashes, parens, country prefix). Against a 4-digit PIN that made
/// the guard bypassable, so this must normalize exactly the way the account lookup does.
///
/// Tenant-scoped because <c>(TenantId, PhoneNumber)</c> is unique per tenant, not globally: two
/// companies can each have an 0501234567. Without the tenant in the key, failing against one
/// company's user would lock out a different company's user — a cross-tenant denial of service.
/// </summary>
public static class LoginIdentity
{
    public static string LockoutKey(Guid tenantId, string? identifier)
    {
        var raw = identifier?.Trim() ?? string.Empty;
        // Reads as a phone → the canonical subscriber number; otherwise treat it as an email.
        var canonical = PhoneNumbers.Normalize(raw) ?? raw.ToLowerInvariant();
        return $"{tenantId:N}:{canonical}";
    }

    /// <summary>
    /// The key app-login (the native app and app.qrlog.az, which have no company subdomain) spends from.
    /// Its own namespace: app-login looks across every company, so it cannot use the per-tenant key, and
    /// the two budgets must not be one. Built here, once, because anything that clears a lockout has to
    /// arrive at exactly the string app-login spent — a clearer that spelled it by hand and drifted would
    /// clear nothing and say nothing.
    /// </summary>
    public static string AppLoginKey(string? identifier) => $"applogin:{LockoutKey(Guid.Empty, identifier)}";
}
