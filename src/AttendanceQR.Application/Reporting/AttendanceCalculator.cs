using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Application.Reporting;

/// <summary>Computed attendance figures for one employee on one day.</summary>
public readonly record struct DayComputation(
    DailySummaryStatus Status,
    int WorkedMinutes,
    int LateMinutes,
    int OvertimeMinutes);

/// <summary>
/// The single place that turns a raw <see cref="AttendanceRecord"/> + a location's shift + the app
/// time zone into a status and the minute figures. Shared by the nightly <c>DailySummaryService</c>
/// (persisted) and the live "today" query (on the fly), so the timezone/late logic exists once.
/// </summary>
public static class AttendanceCalculator
{
    /// <summary>
    /// True when <paramref name="dayOfWeek"/> is a working day per the location's WorkDaysMask
    /// bitmask (bit index = System.DayOfWeek, so Sunday=0 ... Saturday=6).
    /// </summary>
    public static bool IsWorkingDayOfWeek(int workDaysMask, DayOfWeek dayOfWeek)
        => (workDaysMask & (1 << (int)dayOfWeek)) != 0;

    /// <summary>
    /// Whether <paramref name="date"/> is a scheduled working day for this employee, before holidays
    /// are considered.
    ///
    /// An employee on a rotation (<see cref="Employee.WorkCycleDays"/>) ignores the location's weekly
    /// mask entirely — a cycle whose length isn't 7 cannot be expressed as one, so mixing them would
    /// silently drop half the rotation's days. Everyone else falls through to the mask, which is what
    /// every employee did before rotations existed.
    ///
    /// The cycle is anchored to <see cref="Employee.WorkCycleAnchor"/>: that date is day 0 of the
    /// cycle, and the first <see cref="Employee.WorkCycleOnDays"/> days of each repeat are worked.
    /// An incompletely configured rotation (no anchor, or nonsense values an older row could hold)
    /// falls back to the mask rather than marking someone absent on every day of their life.
    /// </summary>
    /// <remarks>
    /// Takes the three cycle values loose rather than an <see cref="Employee"/> because most callers
    /// read employees through a narrow projection, not the whole entity.
    /// </remarks>
    public static bool IsScheduledWorkingDay(
        int? cycleDays, int cycleOnDays, DateOnly? cycleAnchor, int workDaysMask, DateOnly date)
    {
        if (cycleDays is null or < 2 || cycleAnchor is null)
            return IsWorkingDayOfWeek(workDaysMask, date.DayOfWeek);

        var onDays = Math.Clamp(cycleOnDays, 1, cycleDays.Value - 1);

        // DayNumber difference, floored into [0, cycle) so dates BEFORE the anchor land on the right
        // day of the cycle too — C#'s % keeps the sign of the dividend, which would put them outside.
        var offset = date.DayNumber - cycleAnchor.Value.DayNumber;
        var dayInCycle = ((offset % cycleDays.Value) + cycleDays.Value) % cycleDays.Value;

        return dayInCycle < onDays;
    }

    /// <inheritdoc cref="IsScheduledWorkingDay(int?, int, DateOnly?, int, DateOnly)"/>
    public static bool IsScheduledWorkingDay(Employee employee, int workDaysMask, DateOnly date)
        => IsScheduledWorkingDay(
            employee.WorkCycleDays, employee.WorkCycleOnDays, employee.WorkCycleAnchor, workDaysMask, date);

    /// <summary>One continuous stretch of presence — an office scan pair, or one field visit.</summary>
    public readonly record struct WorkSpan(DateTime StartUtc, DateTime EndUtc);

    /// <summary>
    /// How much of a gap BETWEEN two stretches of presence counts as paid travel.
    ///
    /// A worker who checks out of a park at 09:55 and scans in at the base at 10:40 was working for
    /// those 45 minutes — they were driving between two of the company's own sites. But the same gap
    /// shape covers someone who did a 20-minute visit at 09:34 and turned up at the office at 15:00,
    /// and paying five hours for that would be inventing time nobody witnessed.
    ///
    /// So the gap is paid up to this cap and no further: real travel inside Baku is 30–60 minutes, so
    /// an honest journey is paid in full, while anything longer contributes only the cap. Capping
    /// rather than dropping keeps it monotonic — there is no cliff where 59 minutes pays 59 and 61
    /// pays nothing, which would be the one rule guaranteed to feel arbitrary to the person losing it.
    ///
    /// Company-wide for now. If a tenant ever needs its own figure (out-of-town sites would want
    /// more), this is the single place it is read from.
    /// </summary>
    public const int TravelGapCapMinutes = 60;

    /// <summary>
    /// Total worked minutes across every stretch of presence in one day: overlapping stretches are
    /// counted once, and each gap between them is paid up to <see cref="TravelGapCapMinutes"/>.
    ///
    /// This exists because a day is no longer one scan pair. Someone can spend the morning at a site
    /// with no poster, drive to the office, and scan in there — and the reporting used to answer that
    /// with whichever half it happened to prefer: an office scan hid the field hours entirely, and a
    /// field-only day was measured first-arrival-to-last-departure, which silently paid the whole
    /// middle of a day that held two twenty-minute visits.
    /// </summary>
    public static int WorkedMinutesAcross(IEnumerable<WorkSpan> spans, int travelGapCapMinutes = TravelGapCapMinutes)
    {
        // Zero-length and inverted spans are dropped, not clamped: an inverted one means the data is
        // wrong (a hand-edited check-out before its check-in), and guessing what it meant is worse
        // than ignoring it.
        var ordered = spans.Where(s => s.EndUtc > s.StartUtc).OrderBy(s => s.StartUtc).ToList();
        if (ordered.Count == 0)
            return 0;

        var total = 0d;
        var currentStart = ordered[0].StartUtc;
        var currentEnd = ordered[0].EndUtc;

        foreach (var span in ordered.Skip(1))
        {
            if (span.StartUtc <= currentEnd)
            {
                // Overlapping or touching — one stretch. A field visit nested inside office hours
                // lands here, which is exactly why nothing is double-counted.
                if (span.EndUtc > currentEnd) currentEnd = span.EndUtc;
                continue;
            }

            total += (currentEnd - currentStart).TotalMinutes;
            total += Math.Min((span.StartUtc - currentEnd).TotalMinutes, travelGapCapMinutes);
            currentStart = span.StartUtc;
            currentEnd = span.EndUtc;
        }

        total += (currentEnd - currentStart).TotalMinutes;
        return (int)Math.Round(total);
    }

    /// <summary>
    /// Resolves the status to report when an employee has no check-in for the date, in priority
    /// order: an approved LeaveRecord beats everything (even a non-working day — being on
    /// vacation over a weekend still reads as leave, not "day off"), then DayOff on a non-working
    /// day, then plain Absent. Shared by DailySummaryService and ReportQueryService so the two
    /// never resolve this differently.
    /// </summary>
    public static DailySummaryStatus ResolveNoRecordStatus(bool isWorkingDay, LeaveType? leaveType)
    {
        if (leaveType is not null)
            return leaveType switch
            {
                // A rest day is a day off, not planned leave — it shows as İstirahət and, unlike
                // Absent, never costs the employee pay.
                LeaveType.Rest => DailySummaryStatus.DayOff,
                LeaveType.Permission => DailySummaryStatus.Permission,
                // Vacation / Sick / Unpaid / BusinessTrip: all "approved, not an unexcused absence" —
                // OnLeave never deducts pay. BusinessTrip (Ezamiyyət) is the driver-on-a-trip case; it
                // reads OnLeave here and is told apart only in the tabel by its own "Ez" code.
                _ => DailySummaryStatus.OnLeave,
            };
        return isWorkingDay ? DailySummaryStatus.Absent : DailySummaryStatus.DayOff;
    }

    /// <param name="isWorkingDay">
    /// Whether this date is a working day for this location — the location's weekly WorkDaysMask
    /// AND no admin-declared NonWorkingDay for this date/location. Callers compute this (it needs a
    /// NonWorkingDay lookup this method doesn't have access to).
    /// </param>
    /// <param name="noRecordStatus">
    /// Status to report when nobody checked in — Absent on a working day, DayOff otherwise (or,
    /// once leave/permission exists, OnLeave/Permission — the caller decides).
    /// </param>
    /// <param name="shift">
    /// The hours and lateness threshold that apply to this employee, already resolved by
    /// <see cref="EffectiveShift.Resolve(Employee, Schedule?, Location)"/>. Taking it pre-resolved is
    /// deliberate: this method used to fall back to the location itself, which meant the rule for
    /// "whose hours count" existed here AND at the scan endpoint, and the two could drift.
    /// </param>
    public static DayComputation Compute(
        AttendanceRecord? record, EffectiveShift shift, TimeZoneInfo timeZone,
        bool isWorkingDay, DailySummaryStatus noRecordStatus)
    {
        var shiftStart = shift.Start;
        var shiftEnd = shift.End;

        // No record → the employee never showed up (or it wasn't a working day / they were on
        // leave — whichever the caller determined via noRecordStatus).
        if (record is null || record.CheckInAtUtc is null)
            return new DayComputation(noRecordStatus, 0, 0, 0);

        // Checked in but never out.
        if (record.CheckOutAtUtc is null)
            return new DayComputation(DailySummaryStatus.Incomplete, 0, 0, 0);

        // Timezone: CheckInAtUtc is a UTC instant; ShiftStart/ShiftEnd are LOCAL wall-clock times.
        // Convert to local (Asia/Baku = UTC+4) before comparing, otherwise a 05:45Z check-in would
        // look like it beat a 09:00 shift when it is really 09:45 local (45 min late).
        var localCheckIn = TimeZoneInfo.ConvertTimeFromUtc(record.CheckInAtUtc.Value, timeZone);
        var localCheckOut = TimeZoneInfo.ConvertTimeFromUtc(record.CheckOutAtUtc.Value, timeZone);

        // An overnight shift (e.g. 22:00–06:00) is one whose end is EARLIER than its start — it
        // crosses midnight. Put every time-of-day on a single continuous timeline with a NOON PIVOT:
        // for overnight shifts, anything before noon is the *next* day, so 06:00 comes after 22:00.
        // Day shifts leave every value untouched, so their late/overtime is computed exactly as before.
        var overnight = shiftEnd < shiftStart;
        int OnTimeline(TimeOnly t)
        {
            var m = (int)t.ToTimeSpan().TotalMinutes;
            return overnight && m < 12 * 60 ? m + 24 * 60 : m;
        }

        var startMin = OnTimeline(shiftStart);
        var endMin = OnTimeline(shiftEnd);
        var checkInMin = OnTimeline(TimeOnly.FromDateTime(localCheckIn));
        var checkOutMin = OnTimeline(TimeOnly.FromDateTime(localCheckOut));

        var minutesAfterStart = checkInMin - startMin;

        var workedMinutes = (int)Math.Round((record.CheckOutAtUtc.Value - record.CheckInAtUtc.Value).TotalMinutes);

        // A non-working day has no concept of "late" — showing up at all is a bonus, not tardy.
        DailySummaryStatus status;
        var lateMinutes = 0;
        if (isWorkingDay && minutesAfterStart > shift.LateThresholdMinutes)
        {
            status = DailySummaryStatus.Late;
            lateMinutes = minutesAfterStart;
        }
        else
        {
            status = DailySummaryStatus.OnTime;
        }

        var overtime = checkOutMin - endMin;
        var overtimeMinutes = overtime > 0 ? overtime : 0;

        return new DayComputation(status, workedMinutes, lateMinutes, overtimeMinutes);
    }
}
