namespace AttendanceQR.Domain.Enums;

/// <summary>
/// The reason behind a <see cref="Entities.LeaveRecord"/>. Vacation/Sick/Unpaid all map to
/// DailySummaryStatus.OnLeave (they're all "approved planned absence" as far as reporting cares);
/// Permission maps to its own DailySummaryStatus.Permission.
/// </summary>
public enum LeaveType
{
    Vacation = 0,
    Sick = 1,
    Unpaid = 2,
    Permission = 3
}
