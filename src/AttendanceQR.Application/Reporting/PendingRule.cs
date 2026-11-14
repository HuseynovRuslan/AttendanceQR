using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// «Gözlənilir» — a working day whose absence is not real yet. One rule, read by the live board, the
/// tabel and its Excel file, the summary, the payroll, the dashboard and the HR statistics, so no two
/// screens can disagree about the same person at the same minute.
///
/// It exists because «Qayıb» was decided at 00:00. The board learnt to say «not started yet», but every
/// other screen took a scheduled day with no scan as an absence from midnight on: at noon the tabel
/// printed Q for six people whose shift began at 14:00, and the payroll — whose range runs to today —
/// deducted a day's pay from each of them for a shift that had not started.
///
/// The decision (owner, 2026-11-14): the absence becomes real when that day's start time plus the shift's
/// own late threshold has passed. Until then the day is «Gözlənilir»: not an absent day, not deducted,
/// not counted as an absence anywhere — but still a scheduled working day, so the payroll's count of
/// working days does not move. Everything that is not an inferred absence keeps its own status: a scan,
/// a leave, a permission, a rest day, a holiday, and a manager's «Qayıb yaz», which is a statement and
/// is never «Gözlənilir».
/// </summary>
public static class PendingRule
{
    /// <summary>
    /// The moment the day's absence becomes real, in company time: that day's own start (a day the plan
    /// moves moves this with it) plus the shift's late threshold. Deliberately NOT cut at midnight: a
    /// shift starting 23:50 with fifteen minutes' grace is due at 00:05 the next calendar day, and until
    /// then the day it belongs to is still «Gözlənilir».
    /// </summary>
    public static DateTime DueLocal(EffectiveShift shift, DateOnly date) =>
        date.ToDateTime(shift.HoursOn(date).Start).AddMinutes(Math.Max(0, shift.LateThresholdMinutes));

    /// <summary>
    /// Whether a computed day reads «Gözlənilir» at <paramref name="nowLocal"/> (company time). Only an
    /// inferred absence qualifies — every other status is evidence or a decision and stands as it is.
    /// </summary>
    public static bool IsPending(DailySummaryStatus computed, EffectiveShift shift, DateOnly date, DateTime nowLocal) =>
        computed == DailySummaryStatus.Absent && nowLocal <= DueLocal(shift, date);
}
