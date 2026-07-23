namespace AttendanceQR.Domain.Enums;

/// <summary>
/// The reason behind a <see cref="Entities.LeaveRecord"/>. Vacation/Sick/Unpaid all map to
/// DailySummaryStatus.OnLeave (they're all "approved planned absence" as far as reporting cares);
/// Permission maps to its own DailySummaryStatus.Permission; Rest maps to DayOff — a day the person
/// was legitimately off (not their scheduled weekend, but still not an absence), so it must read the
/// same as a rest day rather than a missed shift and never deduct pay.
/// </summary>
public enum LeaveType
{
    Vacation = 0,
    Sick = 1,
    Unpaid = 2,
    Permission = 3,
    Rest = 4
}
