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
    /// <param name="unpaidDays">Ödənişsiz məzuniyyət, in calendar days. A SUBSET of
    /// <paramref name="leaveDays"/>, so it stays inside the divisor — an unpaid working day is still a
    /// scheduled working day — and its working days (less <c>offDayUnpaidDays</c>) are deducted below
    /// alongside the unexcused absences.
    ///
    /// This parameter did not exist, so unpaid leave was PAID AT FULL SALARY. The word means unpaid.
    /// Found 2026-09-04 by an audit of the leave types; production held 23 such days at one company,
    /// every one of them paid. Defaulted so existing positional callers still compile — the
    /// convention used throughout these records.</param>
    public static (int Scheduled, decimal PerDay, decimal Deduction, decimal Payable) Compute(
        decimal salary, int workDays, int absentDays, int leaveDays, int permissionDays, int tripDays,
        int unpaidDays = 0,
        // Leave/permission/trip days that fell on a day this person was NOT scheduled to work — a
        // weekend inside a fortnight's holiday, a bayram inside a business trip.
        //
        // They must come OUT of the divisor. An approved leave beats the day-off rule deliberately
        // (a person on holiday over a Sunday reads as «Məzuniyyət», not «İstirahət» — that is pinned
        // by tests and is right for the board and the tabel), so those Sundays arrive here inside
        // leaveDays. Left in, they inflate the count of "working days in the period", which lowers
        // the daily rate, which under-deducts every real absence the same person had. A fortnight's
        // holiday added four phantom working days to a month.
        //
        // Subtracted rather than never-added, because leaveDays is also a REPORTED figure and must
        // keep counting calendar days: «14 gün məzuniyyət» is 14 days, weekends included.
        int offDayLeaveDays = 0,
        // «Gözlənilir» days (PendingRule): scheduled working days whose shift has not started yet. They
        // go INTO the divisor and NOT into the deduction. The divisor is a count of working days in the
        // period, and the day is one — before this existed it arrived inside absentDays and was counted
        // there; taking it out of the absence without putting it back here would have shrunk the
        // divisor and raised everybody's daily rate until the shift began.
        int pendingDays = 0,
        // The part of unpaidDays that fell on days this person was not scheduled to work — the subset
        // of offDayLeaveDays that is unpaid. Taken OUT of the deduction, exactly as offDayLeaveDays is
        // taken out of the divisor, and for the same reason: unpaidDays is a reported figure and keeps
        // counting calendar days, weekends included. Before this existed a fortnight's unpaid leave
        // (Mon 2 – Sun 15 November) for a Monday-to-Friday worker on 630 was deducted as 14 × 30 = 420:
        // four of those days were Saturdays and Sundays the person is never paid for, so the deduction
        // was 120 more than the ten working days, 300, they actually took off.
        int offDayUnpaidDays = 0,
        // Days WORKED on a day this person was not scheduled to work — an extra Saturday, a bayram they
        // came in on. They arrive inside workDays, which is a reported figure («Gəlib 23») and keeps
        // counting them; the divisor must not. Left in, every extra day lowered the daily rate and so
        // cheapened every absence the same month: a Monday-to-Friday worker on 840 who came in on four
        // Saturdays and missed two weekdays was docked 2 × 840/25 = 67.20 instead of 2 × 840/21 = 80 —
        // and the more extra days somebody gave, the less their absences cost, which is backwards for
        // the person who did not give any.
        int offDayWorkedDays = 0)
    {
        var scheduled = ScheduledDays(
            workDays, absentDays, leaveDays, permissionDays, tripDays, offDayLeaveDays, pendingDays, offDayWorkedDays);
        if (scheduled <= 0)
            return (0, 0m, 0m, salary);   // salary set but no working day fell in the period

        var perDay = Math.Round(salary / scheduled, 2, MidpointRounding.AwayFromZero);
        // Unexcused absence AND unpaid leave. Different facts about a person — one is a no-show, the
        // other agreed in advance — but the same cost: a day that is not paid for. Only the working
        // days of the unpaid leave: a day off costs nothing whatever leave it falls inside.
        var unpaid = DeductibleUnpaidDays(unpaidDays, offDayUnpaidDays);
        var deduction = Math.Round(perDay * (absentDays + unpaid), 2, MidpointRounding.AwayFromZero);
        var payable = salary - deduction;
        return (scheduled, perDay, deduction, payable < 0m ? 0m : payable);
    }

    /// <summary>
    /// The working days in the period — the divisor of the daily rate, printed as «İş günü». Every day the
    /// summary gave a working-day status, less the ones of them that were not working days for this person.
    ///
    /// One expression, read by <see cref="Compute"/> and by the payroll line of somebody with no salary
    /// set (who still gets an «İş günü» figure). It was written out twice, and a term added to one copy and
    /// not the other is precisely how the trip-day divisor went wrong.
    /// </summary>
    public static int ScheduledDays(
        int workDays, int absentDays, int leaveDays, int permissionDays, int tripDays,
        int offDayLeaveDays = 0, int pendingDays = 0, int offDayWorkedDays = 0) =>
        workDays - offDayWorkedDays + absentDays + leaveDays + permissionDays + tripDays - offDayLeaveDays + pendingDays;

    /// <summary>
    /// The unpaid-leave days a deduction is taken for: the calendar days less the ones that were not
    /// working days. One expression, used by <see cref="Compute"/> and by the payroll row that prints
    /// it, so the figure on the sheet is always the one the money was computed from. Never below zero:
    /// a row whose off-day count outran its total (a leave record deleted after the night job ran) is
    /// not a reason to pay somebody extra.
    /// </summary>
    public static int DeductibleUnpaidDays(int unpaidDays, int offDayUnpaidDays) =>
        Math.Max(0, unpaidDays - offDayUnpaidDays);

    /// <summary>
    /// Whether <paramref name="date"/> was a working day for the payroll, given the shift that day was
    /// resolved to (ShiftOverrideMap.ScheduleFor → EffectiveShift.Resolve — the summary job's own
    /// resolution) and whether it was a holiday at the day's branch.
    ///
    /// For a fixed shift it is the summary's question verbatim: <see cref="EffectiveShift.IsWorkingDay"/>
    /// and no holiday — the same answer that made the day «Qayıb» or «İstirahət» in the first place, so
    /// the payroll cannot count a day as due that the board never did, or the other way round.
    ///
    /// A FLEXIBLE shift is the one place it cannot be verbatim. «Elastik qrafik» answers false for every
    /// date on purpose (nobody on it is ever due, so nothing can ever be late or absent) — and taken
    /// literally that would leave such a person with no working day at all, which PayrollMath reads as
    /// "nothing to deduct": their unpaid leave would be paid in full, the exact bug
    /// <see cref="Compute"/>'s unpaidDays exists to stop. So for a flexible shift the payroll falls back
    /// to the shift's own working week, the days in its WorkDaysMask — the same days a daily norm
    /// applies on (<see cref="EffectiveShift.IsNormDay"/>). A plan's «İstirahət» on a flexible shift has
    /// an empty mask and so still reads as a day off.
    /// </summary>
    public static bool IsWorkingDay(EffectiveShift shift, DateOnly date, bool isHoliday)
    {
        if (isHoliday)
            return false;
        if (!shift.IsFlexible)
            return shift.IsWorkingDay(date);
        return AttendanceCalculator.IsWorkingDayOfWeek(shift.WorkDaysMask, date.DayOfWeek);
    }
}
