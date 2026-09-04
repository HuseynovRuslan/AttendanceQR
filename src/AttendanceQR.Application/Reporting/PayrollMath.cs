namespace AttendanceQR.Application.Reporting;

/// <summary>
/// The per-employee payroll arithmetic, pulled out of <c>GetPayrollAsync</c> so it can be tested —
/// which it never was, and the first review of it found live money being taken wrongly.
///
/// The model: a fixed monthly salary, divided by the days that were WORKING days for this person in
/// the period, and one share deducted per unexcused absence. Everything else — leave, permission,
/// business trips, overtime — moves no money here.
/// </summary>
public static class PayrollMath
{
    /// <param name="tripDays">Ezamiyyət. In the divisor DELIBERATELY and the reason this class
    /// exists: when trips were split out of <c>LeaveDays</c> so reports could tell a driver from a
    /// holiday-maker, the divisor was not updated — so every trip day silently SHRANK the divisor,
    /// which inflated the per-day rate, which inflated the deduction for any absent day the same
    /// person had. Someone with 22 worked days, 5 trip days and 1 absence was docked salary/23
    /// per day instead of salary/28 — about 22% over, per absence, since 2026-08-31. A trip is a
    /// working day (the person is WORKING, just somewhere with no poster), so it belongs in the
    /// divisor exactly as the comment above the old code always claimed.</param>
    /// <param name="unpaidDays">Ödənişsiz məzuniyyət. A SUBSET of <paramref name="leaveDays"/>, so it
    /// stays inside the divisor — an unpaid day is still a scheduled working day — and is deducted
    /// below alongside the unexcused absences.
    ///
    /// This parameter did not exist, so unpaid leave was PAID AT FULL SALARY. The word means unpaid.
    /// Found 2026-09-04 by an audit of the leave types; production held 23 such days at one company,
    /// every one of them paid. Defaulted so existing positional callers still compile — the
    /// convention used throughout these records.</param>
    public static (int Scheduled, decimal PerDay, decimal Deduction, decimal Payable) Compute(
        decimal salary, int workDays, int absentDays, int leaveDays, int permissionDays, int tripDays,
        int unpaidDays = 0)
    {
        var scheduled = workDays + absentDays + leaveDays + permissionDays + tripDays;
        if (scheduled <= 0)
            return (0, 0m, 0m, salary);   // salary set but no working day fell in the period

        var perDay = Math.Round(salary / scheduled, 2, MidpointRounding.AwayFromZero);
        // Unexcused absence AND unpaid leave. Different facts about a person — one is a no-show, the
        // other agreed in advance — but the same cost: a day that is not paid for.
        var deduction = Math.Round(perDay * (absentDays + unpaidDays), 2, MidpointRounding.AwayFromZero);
        var payable = salary - deduction;
        return (scheduled, perDay, deduction, payable < 0m ? 0m : payable);
    }
}
