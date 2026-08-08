using AttendanceQR.Domain;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Pins the PUBLISHED package pricing (qrlog.az, owner's numbers 2026-08-08): head-count picks the
/// package, every employee bills at that package's flat rate, each active location adds 5 AZN.
/// Invoices must match the public price list — that is the invariant now, replacing the old
/// graduated-bracket "bill never dips" property: a flat in-package rate deliberately dips at a
/// package boundary (10 → 40 ₼, 11 → 38.5 ₼), because that volume discount IS the published offer.
/// If a test here fails, either the engine or qrlog.az is lying about the price.
/// </summary>
public class PricingTests
{
    [Theory]
    // Start: 1–10 @ 4.0
    [InlineData(0, 0)]
    [InlineData(-5, 0)]         // guard: no negative head-count
    [InlineData(1, 4)]
    [InlineData(10, 40)]
    // Biznes: 11–50 @ 3.5 — the deliberate package-boundary dip (40 → 38.5)
    [InlineData(11, 38.5)]
    [InlineData(50, 175)]
    // Korporativ: 51–100 @ 3.0
    [InlineData(51, 153)]
    [InlineData(100, 300)]
    // Enterprise: 101+ — default 3.0 until a negotiated MonthlyPriceOverride replaces the formula
    [InlineData(101, 303)]
    [InlineData(1000, 3000)]    // ≈ Bakı Abadlıq scale
    public void MonthlyAmount_matches_the_published_package_table(int employees, decimal expected)
    {
        Assert.Equal(expected, Pricing.MonthlyAmount(employees, locations: 0));
    }

    [Theory]
    // Each active location adds a flat 5 ₼ on every package.
    [InlineData(10, 1, 45)]     // 40 + 5
    [InlineData(10, 3, 55)]     // 40 + 15
    [InlineData(50, 2, 185)]    // 175 + 10
    [InlineData(100, 4, 320)]   // 300 + 20
    public void Each_location_adds_the_flat_monthly_fee(int employees, int locations, decimal expected)
    {
        Assert.Equal(expected, Pricing.MonthlyAmount(employees, locations));
    }

    [Fact]
    public void Empty_tenant_bills_zero_even_with_locations()
    {
        // The location fee rides on a live subscription — a company with no active employees is not
        // using the system and gets no standalone location charge.
        Assert.Equal(0m, Pricing.MonthlyAmount(0, 5));
        Assert.Equal(0m, Pricing.MonthlyAmount(-1, 3));
    }

    [Fact]
    public void Negative_location_count_never_discounts_the_bill()
    {
        Assert.Equal(40m, Pricing.MonthlyAmount(10, -2));
    }

    [Theory]
    // The package boundaries, exactly as published: 10|11 and 50|51.
    [InlineData(10, 4.0)]
    [InlineData(11, 3.5)]
    [InlineData(50, 3.5)]
    [InlineData(51, 3.0)]
    [InlineData(100, 3.0)]
    [InlineData(101, 3.0)]
    public void RatePerEmployee_matches_the_package_boundaries(int employees, decimal rate)
    {
        Assert.Equal(rate, Pricing.RatePerEmployee(employees));
    }
}
