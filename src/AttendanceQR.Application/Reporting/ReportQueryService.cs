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

    /// <summary>
    /// KPI tiles, trend/weekday charts, and a top-5-late list over a date range — the richer
    /// dashboard view. Same scope rules as GetSummaryAsync (built on the same DailySummary rows).
    /// </summary>
    Task<(ReportAccess Access, DashboardReport? Report)> GetDashboardAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default);

    /// <summary>
    /// Every rejected scan on a local day (from AuditLogs) — who could not check in/out and why.
    /// Admin sees all; a Manager only employees in their managed locations; an Employee: Forbidden.
    /// </summary>
    Task<(ReportAccess Access, ProblemsReport? Report)> GetProblemsAsync(
        DateOnly date, Guid requesterId, EmployeeRole role, CancellationToken ct = default);
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
                record?.CheckInAtUtc, record?.CheckOutAtUtc,
                record?.Id, record?.CheckInPhotoKey != null,
                record?.FaceMatchScore, record?.FaceMatchStatus.ToString() ?? "NotChecked"));
        }

        return rows.OrderBy(r => r.EmployeeName).ToList();
    }

    public async Task<(ReportAccess Access, ProblemsReport? Report)> GetProblemsAsync(
        DateOnly date, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // An employee has no business seeing everyone else's failed scans.
        if (role == EmployeeRole.Employee)
            return (ReportAccess.Forbidden, null);

        // Audit rows are stamped in UTC; translate the requested LOCAL day into a UTC window.
        var localStart = date.ToDateTime(TimeOnly.MinValue);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), _timeZone);
        var endUtc = startUtc.AddDays(1);

        // A single day of audit rows is small — pull them once and shape in memory.
        var dayLogs = await _db.AuditLogs
            .Where(a => a.CreatedAtUtc >= startUtc && a.CreatedAtUtc < endUtc)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(ct);

        var empIds = dayLogs.Where(a => a.EmployeeId.HasValue).Select(a => a.EmployeeId!.Value).Distinct().ToList();
        var empById = await _db.Employees
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.LocationId })
            .ToDictionaryAsync(e => e.Id, e => (e.FullName, e.LocationId), ct);

        // Manager scope: only employees in the locations they manage. Admin: everything.
        List<Guid>? managed = role == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, requesterId, ct)
            : null;

        bool InScope(Guid? employeeId) =>
            managed is null
            || (employeeId is Guid id && empById.TryGetValue(id, out var e) && managed.Contains(e.LocationId));

        string NameOf(Guid? employeeId) =>
            employeeId is Guid id && empById.TryGetValue(id, out var e) ? e.FullName : "(naməlum)";

        static string ActionOf(AuditEventType type) => type switch
        {
            AuditEventType.CheckInRejected => "CheckIn",
            AuditEventType.CheckOutRejected => "CheckOut",
            _ => "Device"
        };

        // Client-reported reasons may carry a "|detail" suffix (e.g. "GpsInaccurate|520"); strip it
        // off so the per-reason tally below still groups on the bare code.
        static (string Code, string? Detail) SplitReason(string? reason)
        {
            if (string.IsNullOrEmpty(reason)) return ("Unknown", null);
            var sep = reason.IndexOf('|');
            return sep < 0 ? (reason, null) : (reason[..sep], reason[(sep + 1)..]);
        }

        var problems = dayLogs
            .Where(a => a.EventType is AuditEventType.CheckInRejected
                or AuditEventType.CheckOutRejected
                or AuditEventType.ScanBlockedOnDevice)
            .Where(a => InScope(a.EmployeeId))
            .Select(a =>
            {
                var (code, detail) = SplitReason(a.Reason);
                return new ProblemRow(a.CreatedAtUtc, a.EmployeeId, NameOf(a.EmployeeId), ActionOf(a.EventType), code, detail);
            })
            .ToList();

        var successCount = dayLogs.Count(a =>
            (a.EventType is AuditEventType.CheckInSuccess or AuditEventType.CheckOutSuccess)
            && InScope(a.EmployeeId));

        var summary = problems
            .GroupBy(p => p.Reason)
            .Select(g => new ReasonCount(g.Key, g.Count()))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Reason)
            .ToList();

        return (ReportAccess.Allowed, new ProblemsReport(date, problems.Count, successCount, summary, problems));
    }

    public async Task<(ReportAccess Access, DashboardReport? Report)> GetDashboardAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        var baseQuery = _db.DailySummaries.Where(s => s.SummaryDate >= from && s.SummaryDate <= to);

        // Same scope authority as GetSummaryAsync — one rule for every reporting view.
        var scoped = await LocationScope.ApplyLocationScopeAsync(_db, baseQuery, requesterId, role, locationId, ct);
        if (scoped.Access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, null);

        var summaries = await scoped.Query
            .Select(s => new
            {
                s.EmployeeId, s.SummaryDate, s.CheckInAtUtc, s.CheckOutAtUtc,
                s.Status, s.WorkedMinutes, s.OvertimeMinutes, s.LateMinutes
            })
            .ToListAsync(ct);

        // Everything below is scoped to exactly these employees — derived from the same
        // already-scoped DailySummary rows, rather than re-deriving scope rules a second time.
        var scopedEmployeeIds = summaries.Select(s => s.EmployeeId).Distinct().ToList();

        var totalCheckIns = summaries.Count(s => s.CheckInAtUtc != null);
        var totalCheckOuts = summaries.Count(s => s.CheckOutAtUtc != null);
        var lateCount = summaries.Count(s => s.Status == DailySummaryStatus.Late);
        var absentCount = summaries.Count(s => s.Status == DailySummaryStatus.Absent);
        var incompleteCount = summaries.Count(s => s.Status == DailySummaryStatus.Incomplete);
        var dayOffCount = summaries.Count(s => s.Status == DailySummaryStatus.DayOff);
        var leaveCount = summaries.Count(s => s.Status == DailySummaryStatus.OnLeave);
        var permissionCount = summaries.Count(s => s.Status == DailySummaryStatus.Permission);
        var totalWorkedHours = Math.Round(summaries.Sum(s => s.WorkedMinutes) / 60.0, 2);
        var overtimeHours = Math.Round(summaries.Sum(s => s.OvertimeMinutes) / 60.0, 2);

        // DateOnly.ToDateTime returns Kind=Unspecified; Npgsql refuses anything but Kind=Utc for a
        // "timestamptz" column (AuditLog.CreatedAtUtc) — SpecifyKind is required, not cosmetic.
        var rangeStartUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var rangeEndUtc = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var outsideRadiusCount = await _db.AuditLogs
            .Where(a => a.EventType == AuditEventType.CheckInRejected && a.Reason == "OutsideRadius"
                        && a.CreatedAtUtc >= rangeStartUtc && a.CreatedAtUtc < rangeEndUtc
                        && a.EmployeeId != null && scopedEmployeeIds.Contains(a.EmployeeId.Value))
            .CountAsync(ct);

        var activeDeviceCount = await _db.DeviceBindings
            .Where(d => d.IsActive && scopedEmployeeIds.Contains(d.EmployeeId))
            .CountAsync(ct);

        var trend = summaries
            .GroupBy(s => s.SummaryDate)
            .Select(g => new DailyTrendPoint(g.Key, g.Count(x => x.CheckInAtUtc != null), g.Count(x => x.CheckOutAtUtc != null)))
            .OrderBy(p => p.Date)
            .ToList();

        var weekday = summaries
            .GroupBy(s => (int)s.SummaryDate.DayOfWeek)
            .Select(g => new WeekdayPoint(g.Key, g.Count(x => x.CheckInAtUtc != null), g.Count(x => x.CheckOutAtUtc != null)))
            .OrderBy(p => p.DayOfWeek)
            .ToList();

        var employeeNames = await _db.Employees
            .Where(e => scopedEmployeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var topLate = summaries
            .Where(s => s.Status == DailySummaryStatus.Late)
            .GroupBy(s => s.EmployeeId)
            .Select(g => new TopLateRow(g.Key, employeeNames.GetValueOrDefault(g.Key, "—"), g.Count(), g.Sum(x => x.LateMinutes)))
            .OrderByDescending(r => r.TotalLateMinutes)
            .Take(5)
            .ToList();

        var checkInOutRatio = totalCheckIns > 0 ? Math.Round(totalCheckOuts * 100.0 / totalCheckIns, 1) : 0;
        var lateRate = totalCheckIns > 0 ? Math.Round(lateCount * 100.0 / totalCheckIns, 1) : 0;
        var outsideRadiusRate = totalCheckIns > 0 ? Math.Round(outsideRadiusCount * 100.0 / totalCheckIns, 1) : 0;
        var daySpan = Math.Max(1, to.DayNumber - from.DayNumber + 1);
        var avgDailyOperations = Math.Round((totalCheckIns + totalCheckOuts) / (double)daySpan, 1);

        var report = new DashboardReport(
            from, to, scoped.Label,
            totalCheckIns, totalCheckOuts, lateCount, absentCount, incompleteCount, dayOffCount, leaveCount, permissionCount,
            totalWorkedHours, overtimeHours, outsideRadiusCount, activeDeviceCount,
            checkInOutRatio, lateRate, outsideRadiusRate, avgDailyOperations,
            trend, weekday, topLate);

        return (ReportAccess.Allowed, report);
    }
}
