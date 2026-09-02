using AttendanceQR.Application.Reporting;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The first tests the payroll arithmetic has ever had. The first review of that arithmetic
/// (2026-09-02) found it taking real money wrongly — see the trip-day test — which is the whole
/// argument for money math living in a pure function.
/// </summary>
public class PayrollMathTests
{
    [Fact]
    public void A_business_trip_day_is_a_working_day_and_belongs_in_the_divisor()
    {
        // 500 AZN; 22 worked + 5 on trips + 1 absent = 28 scheduled days.
        // The bug: trips were left out, so this person was docked 500/23 = 21.74 per absence
        // instead of 500/28 = 17.86 — about 22% over, for being sent on trips by the company.
        var (scheduled, perDay, deduction, _) = PayrollMath.Compute(500m, 22, 1, 0, 0, 5);
        Assert.Equal(28, scheduled);
        Assert.Equal(17.86m, perDay);
        Assert.Equal(17.86m, deduction);
    }

    [Fact]
    public void Leave_and_permission_do_not_deduct_but_do_count_as_scheduled()
    {
        var (scheduled, perDay, deduction, payable) = PayrollMath.Compute(600m, 18, 2, 3, 1, 0);
        Assert.Equal(24, scheduled);
        Assert.Equal(25.00m, perDay);
        Assert.Equal(50.00m, deduction);   // only the 2 unexcused days
        Assert.Equal(550.00m, payable);
    }

    [Fact]
    public void No_scheduled_days_means_nothing_to_deduct()
    {
        // Salary set, but the whole period was day-off (e.g. hired for next month).
        var (scheduled, _, deduction, payable) = PayrollMath.Compute(400m, 0, 0, 0, 0, 0);
        Assert.Equal(0, scheduled);
        Assert.Equal(0m, deduction);
        Assert.Equal(400m, payable);
    }

    [Fact]
    public void Payable_never_goes_below_zero()
    {
        // The negative case comes from ROUNDING, not arithmetic: 100/7 rounds up to 14.29, and
        // seven absences deduct 7 × 14.29 = 100.03 — three qəpik more than the salary itself.
        // Floor at zero: an absence ledger must never turn into a debt the worker owes.
        var (_, perDay, deduction, payable) = PayrollMath.Compute(100m, 0, 7, 0, 0, 0);
        Assert.Equal(14.29m, perDay);
        Assert.Equal(100.03m, deduction);
        Assert.Equal(0m, payable);
    }

    [Fact]
    public void Rounding_is_away_from_zero_at_the_half_qepik()
    {
        // 500/3 = 166.666… → 166.67; matches the accountant's convention the old code used.
        var (_, perDay, _, _) = PayrollMath.Compute(500m, 2, 1, 0, 0, 0);
        Assert.Equal(166.67m, perDay);
    }
}
