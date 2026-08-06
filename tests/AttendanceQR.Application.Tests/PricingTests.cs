using AttendanceQR.Domain;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The graduated per-employee price. The whole point of the bracket model was to kill the tier cliff —
/// where hiring the 31st person made the bill DROP because the flat rate stepped down for everyone. So
/// beyond the exact band boundaries, the load-bearing property is that the bill only ever goes up.
/// </summary>
public class PricingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]        // guard: no negative head-count
    [InlineData(1, 4)]
    [InlineData(30, 120)]      // 30 × 4.0
    [InlineData(31, 123.5)]    // 120 + 1 × 3.5 — one more than 30 costs MORE, not less
    [InlineData(100, 365)]     // 120 + 70 × 3.5
    [InlineData(101, 368)]     // 365 + 1 × 3.0
    [InlineData(500, 1565)]    // 365 + 400 × 3.0
    [InlineData(501, 1568)]    // 1565 + 1 × 3.0
    [InlineData(1000, 3065)]   // 1565 + 500 × 3.0  (≈ Baki Abadliq scale)
    public void MonthlyAmount_matches_the_bracket_table(int employees, decimal expected)
    {
        Assert.Equal(expected, Pricing.MonthlyAmount(employees));
    }

    [Fact]
    public void MonthlyAmount_never_drops_as_head_count_grows()
    {
        // Sweep across every band boundary; each extra employee must never lower the bill (no cliff).
        var prev = Pricing.MonthlyAmount(0);
        for (var n = 1; n <= 600; n++)
        {
            var cur = Pricing.MonthlyAmount(n);
            Assert.True(cur > prev, $"bill fell going from {n - 1} to {n} employees ({prev} → {cur})");
            prev = cur;
        }
    }
}
