using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Reporting;

public interface IReportQueryService
{
    /// <summary>
    /// Reads pre-computed summaries for the period, scoped to what the caller may see. Returns
    /// (Forbidden, null) when the caller requests a location outside their scope.
    /// </summary>
    Task<(ReportAccess Access, AttendanceReport? Report)> GetSummaryAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default);
}

public sealed class ReportQueryService : IReportQueryService
{
    private readonly AppDbContext _db;

    public ReportQueryService(AppDbContext db) => _db = db;

    public async Task<(ReportAccess Access, AttendanceReport? Report)> GetSummaryAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        var baseQuery = _db.DailySummaries.Where(s => s.SummaryDate >= from && s.SummaryDate <= to);

        // Same scope authority for JSON and Excel — export cannot bypass it.
        var scoped = await LocationScope.ApplyLocationScopeAsync(_db, baseQuery, requesterId, role, locationId, ct);
        if (scoped.Access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, null);

        var rows = await (
            from s in scoped.Query
            join e in _db.Employees on s.EmployeeId equals e.Id
            join l in _db.Locations on s.LocationId equals l.Id
            select new { s.EmployeeId, e.FullName, LocationName = l.Name, s.Status, s.WorkedMinutes, s.OvertimeMinutes })
            .ToListAsync(ct);

        var grouped = rows
            .GroupBy(x => new { x.EmployeeId, x.FullName })
            .Select(g => new EmployeeReportRow(
                g.Key.EmployeeId,
                g.Key.FullName,
                g.Select(x => x.LocationName).First(),
                WorkDays: g.Count(x => x.Status != DailySummaryStatus.Absent),
                LateCount: g.Count(x => x.Status == DailySummaryStatus.Late),
                AbsentDays: g.Count(x => x.Status == DailySummaryStatus.Absent),
                IncompleteDays: g.Count(x => x.Status == DailySummaryStatus.Incomplete),
                TotalWorkedHours: Math.Round(g.Sum(x => x.WorkedMinutes) / 60.0, 2),
                OvertimeHours: Math.Round(g.Sum(x => x.OvertimeMinutes) / 60.0, 2)))
            .OrderBy(r => r.EmployeeName)
            .ToList();

        var totals = new ReportTotals(
            WorkDays: grouped.Sum(r => r.WorkDays),
            LateCount: grouped.Sum(r => r.LateCount),
            AbsentDays: grouped.Sum(r => r.AbsentDays),
            IncompleteDays: grouped.Sum(r => r.IncompleteDays),
            TotalWorkedHours: Math.Round(grouped.Sum(r => r.TotalWorkedHours), 2),
            OvertimeHours: Math.Round(grouped.Sum(r => r.OvertimeHours), 2));

        return (ReportAccess.Allowed, new AttendanceReport(from, to, scoped.Label, grouped, totals));
    }
}
