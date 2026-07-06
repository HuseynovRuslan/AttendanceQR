using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
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

    /// <summary>Locations the caller may see/filter by (Admin=all, Manager=managed, Employee=own).</summary>
    Task<IReadOnlyList<LocationDto>> GetVisibleLocationsAsync(
        Guid requesterId, EmployeeRole role, CancellationToken ct = default);

    /// <summary>
    /// Live "today" board computed from raw AttendanceRecords (NOT DailySummary, which only exists
    /// for past days after the nightly job). One row per in-scope active employee.
    /// </summary>
    Task<IReadOnlyList<DayAttendanceRow>> GetTodayAttendanceAsync(
        Guid requesterId, EmployeeRole role, CancellationToken ct = default);
}

public sealed class ReportQueryService : IReportQueryService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;

    public ReportQueryService(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

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
                WorkDays: g.Count(x => x.Status is DailySummaryStatus.OnTime or DailySummaryStatus.Late or DailySummaryStatus.Incomplete),
                LateCount: g.Count(x => x.Status == DailySummaryStatus.Late),
                AbsentDays: g.Count(x => x.Status == DailySummaryStatus.Absent),
                IncompleteDays: g.Count(x => x.Status == DailySummaryStatus.Incomplete),
                TotalWorkedHours: Math.Round(g.Sum(x => x.WorkedMinutes) / 60.0, 2),
                OvertimeHours: Math.Round(g.Sum(x => x.OvertimeMinutes) / 60.0, 2),
                LeaveDays: g.Count(x => x.Status == DailySummaryStatus.OnLeave),
                PermissionDays: g.Count(x => x.Status == DailySummaryStatus.Permission)))
            .OrderBy(r => r.EmployeeName)
            .ToList();

        var totals = new ReportTotals(
            WorkDays: grouped.Sum(r => r.WorkDays),
            LateCount: grouped.Sum(r => r.LateCount),
            AbsentDays: grouped.Sum(r => r.AbsentDays),
            IncompleteDays: grouped.Sum(r => r.IncompleteDays),
            TotalWorkedHours: Math.Round(grouped.Sum(r => r.TotalWorkedHours), 2),
            OvertimeHours: Math.Round(grouped.Sum(r => r.OvertimeHours), 2),
            LeaveDays: grouped.Sum(r => r.LeaveDays),
            PermissionDays: grouped.Sum(r => r.PermissionDays));

        return (ReportAccess.Allowed, new AttendanceReport(from, to, scoped.Label, grouped, totals));
    }

    public async Task<IReadOnlyList<LocationDto>> GetVisibleLocationsAsync(
        Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        IQueryable<Domain.Entities.Location> query = _db.Locations;

        if (role == EmployeeRole.Manager)
        {
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, requesterId, ct);
            query = query.Where(l => managed.Contains(l.Id));
        }
        else if (role == EmployeeRole.Employee)
        {
            var ownLocation = await _db.Employees
                .Where(e => e.Id == requesterId)
                .Select(e => (Guid?)e.LocationId)
                .FirstOrDefaultAsync(ct);
            query = query.Where(l => ownLocation != null && l.Id == ownLocation);
        }
        // Admin → all locations.

        return await query
            .OrderBy(l => l.Name)
            .Select(l => new LocationDto(l.Id, l.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DayAttendanceRow>> GetTodayAttendanceAsync(
        Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        // In-scope active employees. Admins are system operators, not on-site staff — they never
        // check in/out, so including them here would just show a permanent, meaningless "Qayıb".
        var employeesQuery = _db.Employees.Where(e => e.IsActive && e.ActivatedAtUtc != null && e.Role != EmployeeRole.Admin);
        if (role == EmployeeRole.Manager)
        {
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, requesterId, ct);
            employeesQuery = employeesQuery.Where(e => managed.Contains(e.LocationId));
        }
        else if (role == EmployeeRole.Employee)
        {
            employeesQuery = employeesQuery.Where(e => e.Id == requesterId);
        }

        var employees = await employeesQuery
            .Select(e => new { e.Id, e.FullName, e.LocationId })
            .ToListAsync(ct);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();
        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);
        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == today && employeeIds.Contains(r.EmployeeId))
            .ToDictionaryAsync(r => r.EmployeeId, ct);

        // Same non-working-day resolution as the nightly job, just for "today" — so the live
        // board and the persisted summary always agree once the nightly job catches up.
        var nonWorkingLocationIds = await _db.NonWorkingDays
            .Where(n => n.Date == today && (n.LocationId == null || locationIds.Contains(n.LocationId.Value)))
            .Select(n => n.LocationId)
            .ToListAsync(ct);
        var isGloballyNonWorking = nonWorkingLocationIds.Contains(null);
        var nonWorkingLocationIdSet = nonWorkingLocationIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        // Same leave/permission resolution as the nightly job, for "today".
        var leaveByEmployee = await _db.LeaveRecords
            .Where(l => l.FromDate <= today && l.ToDate >= today && employeeIds.Contains(l.EmployeeId))
            .GroupBy(l => l.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Type = g.First().Type })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Type, ct);

        var rows = new List<DayAttendanceRow>(employees.Count);
        foreach (var e in employees)
        {
            if (!locations.TryGetValue(e.LocationId, out var location))
                continue;
            records.TryGetValue(e.Id, out var record);
            var isWorkingDay = AttendanceCalculator.IsWorkingDayOfWeek(location.WorkDaysMask, today.DayOfWeek)
                                && !isGloballyNonWorking
                                && !nonWorkingLocationIdSet.Contains(location.Id);
            LeaveType? leaveType = leaveByEmployee.TryGetValue(e.Id, out var lt) ? lt : null;
            var noRecordStatus = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leaveType);
            var c = AttendanceCalculator.Compute(record, location, _timeZone, isWorkingDay, noRecordStatus);
            rows.Add(new DayAttendanceRow(
                e.Id, e.FullName, location.Id, location.Name, c.Status.ToString(),
                record?.CheckInAtUtc, record?.CheckOutAtUtc));
        }

        return rows.OrderBy(r => r.EmployeeName).ToList();
    }
}
