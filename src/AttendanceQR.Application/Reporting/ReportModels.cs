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
    double OvertimeHours);

/// <summary>Column totals across all rows.</summary>
public sealed record ReportTotals(
    int WorkDays,
    int LateCount,
    int AbsentDays,
    int IncompleteDays,
    double TotalWorkedHours,
    double OvertimeHours);

/// <summary>The full report payload, shared by the JSON and Excel endpoints.</summary>
public sealed record AttendanceReport(
    DateOnly From,
    DateOnly To,
    string ScopeLabel,
    IReadOnlyList<EmployeeReportRow> Rows,
    ReportTotals Totals);
