namespace AttendanceQR.Application.Reporting;

/// <summary>Whether the caller is allowed to see the requested scope.</summary>
public enum ReportAccess
{
    Allowed,
    Forbidden
}

/// <summary>One employee's totals over the report period.</summary>
public sealed record EmployeeReportRow(
    Guid EmployeeId,
    string EmployeeName,
    string LocationName,
    int WorkDays,
    int LateCount,
    int AbsentDays,
    int IncompleteDays,
    double TotalWorkedHours,
    double OvertimeHours,
    int LeaveDays,
    int PermissionDays);

/// <summary>Column totals across all rows.</summary>
public sealed record ReportTotals(
    int WorkDays,
    int LateCount,
    int AbsentDays,
    int IncompleteDays,
    double TotalWorkedHours,
    double OvertimeHours,
    int LeaveDays,
    int PermissionDays);

/// <summary>The full report payload, shared by the JSON and Excel endpoints.</summary>
public sealed record AttendanceReport(
    DateOnly From,
    DateOnly To,
    string ScopeLabel,
    IReadOnlyList<EmployeeReportRow> Rows,
    ReportTotals Totals);

/// <summary>A location the caller may see/filter by (invite + report filter dropdowns).</summary>
public sealed record LocationDto(Guid Id, string Name);

/// <summary>One employee's payroll line for the period, on the fixed-monthly-salary model. Start from
/// the monthly salary, deduct a per-day share for each unexcused absence. Leave/permission days are
/// excused (not deducted) but still count as working days for the divisor. Overtime is carried as
/// hours only — never auto-converted to money.</summary>
public sealed record PayrollRow(
    Guid EmployeeId,
    string EmployeeName,
    string LocationName,
    decimal? MonthlySalary,
    int ScheduledDays,
    int WorkDays,
    int AbsentDays,
    int LeaveDays,
    int PermissionDays,
    double OvertimeHours,
    decimal PerDay,
    decimal Deduction,
    decimal Payable);

/// <summary>The payroll report payload, shared by the JSON and Excel endpoints.</summary>
public sealed record PayrollReport(
    DateOnly From,
    DateOnly To,
    string ScopeLabel,
    IReadOnlyList<PayrollRow> Rows,
    decimal TotalMonthlySalary,
    decimal TotalDeduction,
    decimal TotalPayable);

/// <summary>One employee's live status for a single day (the "today" board).</summary>
public sealed record DayAttendanceRow(
    Guid EmployeeId,
    string EmployeeName,
    Guid LocationId,
    string LocationName,
    string Status,
    DateTime? CheckInAtUtc,
    DateTime? CheckOutAtUtc,
    // Photo-audit: this day's record id (null if the employee has no record yet) and whether that
    // record carries a check-in selfie — lets the Today board deep-link into the photo comparison
    // without an extra round-trip. No new storage: both derive from the already-loaded record.
    Guid? RecordId,
    bool HasPhoto,
    // Face-audit: similarity (0–100, null if not compared) + status string, for the flag badge.
    int? FaceMatchScore,
    string FaceMatchStatus,
    // Reasons the employee gave at the scan for arriving late / leaving early (null if none/skipped).
    string? LateArrivalReason = null,
    string? EarlyDepartureReason = null,
    // True when the record was captured offline and synced later — its time is the phone's clock, so
    // the admin can treat it with a touch more scepticism. See ProcessedScan / the Scan handler.
    bool WasOffline = false);

/// <summary>One rejected scan — a row of the "Problems" screen (who couldn't scan, when, and why).</summary>
public sealed record ProblemRow(
    DateTime AtUtc,
    Guid? EmployeeId,
    string EmployeeName,
    string Action,   // "CheckIn" | "CheckOut" | "Device" (blocked on the phone, never reached us)
    string Reason,
    // Extra context some reasons carry — e.g. the ± metres behind "GpsInaccurate".
    string? Detail = null);

public sealed record ReasonCount(string Reason, int Count);

/// <summary>All rejected scans for one local day, plus a per-reason tally and the success count.</summary>
public sealed record ProblemsReport(
    DateOnly Date,
    int RejectedCount,
    int SuccessCount,
    IReadOnlyList<ReasonCount> Summary,
    IReadOnlyList<ProblemRow> Rows);

/// <summary>One date's check-in/check-out counts, for the dashboard trend chart.</summary>
public sealed record DailyTrendPoint(DateOnly Date, int CheckIns, int CheckOuts);

/// <summary>Check-in/check-out counts summed by day-of-week (0=Sunday..6=Saturday) across the
/// whole report range, for the "weekday pattern" bar chart.</summary>
public sealed record WeekdayPoint(int DayOfWeek, int CheckIns, int CheckOuts);

/// <summary>One employee's lateness within the report range, for the TOP-5 late list.</summary>
public sealed record TopLateRow(Guid EmployeeId, string EmployeeName, int LateCount, int TotalLateMinutes);

/// <summary>The rich dashboard payload — KPI tiles, trend/weekday charts, and a top-late list, all
/// over the requested date range and scope.</summary>
public sealed record DashboardReport(
    DateOnly From,
    DateOnly To,
    string ScopeLabel,
    int TotalCheckIns,
    int TotalCheckOuts,
    int LateCount,
    int AbsentCount,
    /// <summary>Checked in, never checked out, on a day that is OVER — someone genuinely forgot, and
    /// the day reads as zero hours until an admin closes it. Today is excluded on purpose: see
    /// <see cref="StillAtWorkCount"/>.</summary>
    int IncompleteCount,
    /// <summary>Checked in and not yet out TODAY — still at work, which is not a problem and must not
    /// be counted as a forgotten check-out. Always 0 when the range does not include today.</summary>
    int StillAtWorkCount,
    int DayOffCount,
    int LeaveCount,
    int PermissionCount,
    double TotalWorkedHours,
    double OvertimeHours,
    int OutsideRadiusCount,
    int ActiveDeviceCount,
    double CheckInOutRatio,
    double LateRate,
    double OutsideRadiusRate,
    double AvgDailyOperations,
    IReadOnlyList<DailyTrendPoint> Trend,
    IReadOnlyList<WeekdayPoint> WeekdayBreakdown,
    IReadOnlyList<TopLateRow> TopLate);
