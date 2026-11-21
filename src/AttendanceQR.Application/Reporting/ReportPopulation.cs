using System.Linq.Expressions;
using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Reporting;

/// <summary>
/// WHO a report is about — one rule, read by the two places that answer it: the live day
/// (ReportQueryService.ScopedEmployeesAsync — board, tabel, payroll, dashboard, management report) and
/// the finished day (DailySummaryService). They must select the same people, or a day's totals shift
/// depending on which side of midnight somebody asks; written twice, the two copies had already been
/// kept in step by hand.
///
/// Left out: an inactive or never-activated account, the system/root accounts named in HiddenEmails, and
/// an account marked <see cref="Employee.PanelOnly"/> — somebody who runs the panel, not staff.
/// </summary>
public static class ReportPopulation
{
    public static Expression<Func<Employee, bool>> Members(string[] hiddenEmails) =>
        e => e.IsActive
             && e.ActivatedAtUtc != null
             && !e.PanelOnly
             && (e.Email == null || !hiddenEmails.Contains(e.Email.ToLower()));
}
