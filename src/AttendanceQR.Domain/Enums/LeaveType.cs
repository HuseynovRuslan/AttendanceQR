namespace AttendanceQR.Domain.Enums;

/// <summary>
/// The reason behind a <see cref="Entities.LeaveRecord"/>. Vacation/Sick/Unpaid/BusinessTrip all map
/// to DailySummaryStatus.OnLeave (they're all "approved, not an unexcused absence" as far as reporting
/// cares — none deduct pay, all count toward the payroll divisor); Permission maps to its own
/// DailySummaryStatus.Permission; Rest maps to DayOff — a day the person was legitimately off (not
/// their scheduled weekend, but still not an absence), so it must read the same as a rest day rather
/// than a missed shift and never deduct pay.
///
/// BusinessTrip (Ezamiyyət) is a work trip — the employee IS working, just away from the scan poster
/// (a truck driver on a multi-day route, someone sent to another district). It must never read as
/// Qayıb and never dock pay, but the tabel gives it its own code ("Ez") rather than folding it into
/// "İşlədi", so a timesheet still shows WHY there was no scan that day.
/// </summary>
public enum LeaveType
{
    Vacation = 0,
    Sick = 1,
    Unpaid = 2,
    Permission = 3,
    Rest = 4,
    BusinessTrip = 5
}
