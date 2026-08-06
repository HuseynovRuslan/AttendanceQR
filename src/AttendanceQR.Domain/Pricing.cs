namespace AttendanceQR.Domain;

/// <summary>
/// The platform's per-employee pricing, as GRADUATED (marginal) brackets — like income-tax bands, so
/// adding the 31st employee never makes the bill smaller than it was at 30 (the tier-cliff the packaged
/// pricing was designed to avoid). Rates are AZN / employee / month. A tenant with a negotiated flat
/// price (<c>Tenant.MonthlyPriceOverride</c>) bypasses this entirely — big accounts are hand-priced.
/// </summary>
public static class Pricing
{
    // (upTo, rate): the band covers employees from the previous threshold up to and including upTo.
    // 1–30 @ 4.0 ; 31–100 @ 3.5 ; 101–500 @ 3.0 ; 501+ @ 3.0
    private static readonly (int UpTo, decimal Rate)[] Bands =
    {
        (30, 4.0m),
        (100, 3.5m),
        (500, 3.0m),
        (int.MaxValue, 3.0m),
    };

    /// <summary>Monthly amount (AZN) for a given active-employee count, summed across the bands.</summary>
    public static decimal MonthlyAmount(int employees)
    {
        if (employees <= 0) return 0m;
        decimal total = 0m;
        var prev = 0;
        foreach (var (upTo, rate) in Bands)
        {
            if (employees <= prev) break;
            var inBand = Math.Min(employees, upTo) - prev;
            total += inBand * rate;
            prev = upTo;
        }
        return total;
    }
}
