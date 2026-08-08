namespace AttendanceQR.Domain;

/// <summary>
/// The platform's pricing — the PUBLISHED package model from qrlog.az (owner's numbers, 2026-08-08):
/// head-count picks the package, every employee is billed at that package's flat rate, and each
/// active location adds a fixed monthly fee. Invoices must match the public price list, so this
/// replaced the earlier graduated (income-tax-style) brackets.
///
///   Start       1–10 işçi     4.0 AZN / employee / month
///   Biznes      11–50         3.5
///   Korporativ  51–100        3.0
///   Enterprise  101+          "fərdi" on the site — billed at 3.0 here by default; a negotiated
///                             price belongs in <c>Tenant.MonthlyPriceOverride</c>, which bypasses
///                             this formula entirely (big accounts are hand-priced).
///   + 5 AZN / month per active location, on every package.
///
/// DELIBERATE: a flat in-package rate means the bill can DIP at a package boundary (10 işçi = 40,
/// 11 işçi = 38.5, plus location fees) — that volume discount IS the published offer, unlike the old
/// bracket model that was built to avoid it. Do not "fix" the dip without changing qrlog.az first:
/// the failure mode that matters is an invoice that contradicts the public price list.
/// </summary>
public static class Pricing
{
    /// <summary>Monthly fee per active location (branch / QR poster site), same on every package.</summary>
    public const decimal LocationMonthlyFee = 5.0m;

    /// <summary>The package's flat per-employee monthly rate for a given head-count.</summary>
    public static decimal RatePerEmployee(int employees) => employees switch
    {
        <= 10 => 4.0m,
        <= 50 => 3.5m,
        _ => 3.0m,
    };

    /// <summary>
    /// Monthly amount (AZN) for an active-employee count + active-location count. An empty tenant
    /// (no active employees) bills 0 — the location fee rides on a live subscription, it is not a
    /// standalone charge on a company that isn't using the system.
    /// </summary>
    public static decimal MonthlyAmount(int employees, int locations)
    {
        if (employees <= 0) return 0m;
        return employees * RatePerEmployee(employees) + Math.Max(0, locations) * LocationMonthlyFee;
    }
}
