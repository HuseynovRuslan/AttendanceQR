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
    // The site the employee is assigned to — so a geofence problem that hits one location (a poster
    // beyond the radius) is visible as such rather than scattered across a name list.
    string LocationName,
    // "Scan" = rejected before we could tell check-in from check-out (geofence/device/token all fail
    // ahead of that decision, so labelling them "Giriş" was misleading). "CheckOut" = a genuine
    // check-out attempt (the employee already had an open record). "Device" = blocked on the phone.
    string Action,
    string Reason,
    // Extra context some reasons carry — e.g. the ± metres behind "GpsInaccurate".
    string? Detail = null);

public sealed record ReasonCount(string Reason, int Count);

/// <summary>A location's geofence circle, so the Problems map can draw the boundary the rejected
/// points fall outside of. Only emitted for sites that actually had an OutsideRadius rejection.</summary>
public sealed record MapGeofence(string LocationName, double Latitude, double Longitude, int RadiusMeters);

/// <summary>Every rejected scan across a date RANGE, plus a per-reason tally and the success count.
/// A range, not a single day, because a problem two days old is invisible on a one-day view — which
/// is how a whole location's geofence failures and days of forgotten check-outs went unnoticed.</summary>
public sealed record ProblemsReport(
    DateOnly From,
    DateOnly To,
    int RejectedCount,
    int SuccessCount,
    IReadOnlyList<ReasonCount> Summary,
    IReadOnlyList<ProblemRow> Rows,
    // Geofence circles for the sites that had an OutsideRadius rejection — the map draws these, and
    // plots each rejected scan (its lat/lng carried in the row's Detail) against them.
    IReadOnlyList<MapGeofence> Geofences);

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

/// <summary>
/// The monthly attendance timesheet ("Aylıq Tabel") — the grid an accountant reconciles at month end:
/// employees down the side, the days of the month across the top, one code per cell.
///
/// The codes follow the Azerbaijani T-13 convention so a bookkeeper recognises them without a key,
/// and each is derived from data the system already holds — a check-in, an approved leave, a declared
/// holiday, the location's work-day mask — rather than typed by hand. See ReportQueryService for the
/// precedence between them (showing up beats everything: a scan on a leave day is worked time).
/// </summary>
public sealed record TabelReport(
    int Year,
    int Month,
    string ScopeLabel,
    int DaysInMonth,
    IReadOnlyList<TabelRow> Rows,
    IReadOnlyList<TabelLegendItem> Legend);

public sealed record TabelRow(
    Guid EmployeeId,
    string EmployeeName,
    string? Position,
    string LocationName,
    // One code per day of the month, index 0 = day 1. Empty string for days outside the employee's
    // employment is not used — every day of the month gets a code.
    IReadOnlyList<string> Days,
    // Month totals the accountant actually copies out.
    int WorkedDays,
    int AbsentDays,
    int LeaveDays,
    double WorkedHours);

public sealed record TabelLegendItem(string Code, string Label);
