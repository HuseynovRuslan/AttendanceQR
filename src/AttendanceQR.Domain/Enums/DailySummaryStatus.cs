namespace AttendanceQR.Domain.Enums;

public enum DailySummaryStatus
{
    // Check-in and check-out both present, on time.
    OnTime = 0,
    // Check-in and check-out both present, but late past the shift's threshold.
    Late = 1,
    // No attendance record for the day at all.
    Absent = 2,
    // Checked in but never checked out.
    Incomplete = 3,
    // No record, but the day wasn't a working day (weekend or an admin-declared non-working
    // day) — distinct from Absent so a weekend doesn't read as a missed shift.
    DayOff = 4,
    // No record, but the employee has an approved LeaveRecord (Vacation/Sick/Unpaid) covering
    // this date.
    OnLeave = 5,
    // No record, but the employee has an approved LeaveRecord of type Permission covering this
    // date — a short excused absence (e.g. the "worked Sunday, off one weekday instead" case).
    Permission = 6
}
