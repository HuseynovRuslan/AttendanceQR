using AttendanceQR.Application.Reporting;

namespace AttendanceQR.Api.Notifications;

/// <summary>
/// «İşçi gecikdi» — how late an arrival was by the person's OWN shift that day, or null when lateness does not apply.
///
/// The old «Gecikmə» display was removed because it measured everybody against the branch's general hours, and
/// every employee keeps their own. This measures against the shift the scan and the reports already judge the day by
/// (<see cref="EffectiveShift"/>, resolved through the day's plan, a cover, a rotation or personal hours), and only
/// reports a delay past that shift's own grace (<see cref="EffectiveShift.LateThresholdMinutes"/>) — the same line
/// <c>DetermineStatus</c> draws. The minutes are counted from the shift's start, as the reports count them.
///
/// Never late:
///   • a flexible schedule («Elastik qrafik») — there is no hour to be late for;
///   • a day that is not a working day for this person (rest day, rotation off-day), a holiday, a leave day;
///   • a second stretch of the same day (back after a field visit, a re-entry), unless the shift has a second window —
///     then it is judged against that window's start, not the morning's.
/// Pure, so the rule is tested without a database.
/// </summary>
public static class NotificationLateness
{
    public static int? MinutesLate(
        EffectiveShift shift, DateOnly day, DateTime checkInUtc, TimeZoneInfo timeZone, bool dayOff, bool earlierStretch)
    {
        if (shift.IsFlexible || dayOff || !shift.IsWorkingDay(day))
            return null;

        var (firstStart, _) = shift.HoursOn(day);
        DateTime due;
        if (!earlierStretch)
        {
            due = day.ToDateTime(firstStart);
        }
        else
        {
            if (shift.SecondStart is not TimeOnly second || shift.SecondEnd is null)
                return null;
            // A second window that starts before the first is on the next morning (an evening + night split).
            due = day.ToDateTime(second);
            if (second < firstStart)
                due = due.AddDays(1);
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(checkInUtc, DateTimeKind.Utc), timeZone);
        var late = local - due;
        if (late <= TimeSpan.FromMinutes(shift.LateThresholdMinutes))
            return null;
        return (int)Math.Floor(late.TotalMinutes);
    }
}
