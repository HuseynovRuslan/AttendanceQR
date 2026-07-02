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
    Incomplete = 3
}
