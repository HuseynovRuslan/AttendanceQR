namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Which column of the day one person belongs in — the server's copy of <c>bucketOf()</c> in
/// <c>frontend/src/pages/admin/todayCounts.ts</c>.
///
/// It exists because the daily management report is built on the server (a scheduled job has no
/// browser), and the leadership must read the SAME numbers the board shows. Two copies of a rule is
/// exactly the drift today-bucketing-one-rule warns about, so the copies are tied together: both are
/// tested against one fixture, <c>tests/fixtures/today-buckets.json</c> (xunit here, vitest there).
/// Change one without the other and a test fails. Keep the order of the checks identical to the TS.
/// </summary>
public static class TodayBuckets
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string Pending = "pending";
    public const string Onboarding = "onboarding";
    public const string Incomplete = "incomplete";
    public const string DayOff = "dayOff";
    public const string Rest = "rest";
    public const string FlexNoScan = "flexNoScan";
    public const string OnLeave = "onLeave";
    public const string Unpaid = "unpaid";
    public const string Sick = "sick";
    public const string Trip = "trip";
    public const string Permission = "permission";

    public static string BucketOf(string status, string? leaveType, DateTime? fieldCheckOutAtUtc, bool isFlexible)
    {
        if (status == "Field") return fieldCheckOutAtUtc != null ? Present : Incomplete;
        if (status is "OnTime" or "Late") return Present;
        if (status == "Absent") return Absent;
        if (status == "Pending") return Pending;
        if (status == "Onboarding") return Onboarding;
        if (status == "DayOff" && isFlexible && string.IsNullOrEmpty(leaveType)) return FlexNoScan;
        if (status == "DayOff") return leaveType == "Rest" ? Rest : DayOff;
        if (status == "OnLeave")
        {
            if (leaveType == "Sick") return Sick;
            if (leaveType == "BusinessTrip") return Trip;
            if (leaveType == "Unpaid") return Unpaid;
            return OnLeave;
        }
        if (status == "Permission") return Permission;
        // Checked in with no check-out yet: «İşdə» on today's board.
        return Incomplete;
    }

    public static string BucketOf(DayAttendanceRow r)
        => BucketOf(r.Status, r.LeaveType, r.FieldCheckOutAtUtc, r.IsFlexible);
}
