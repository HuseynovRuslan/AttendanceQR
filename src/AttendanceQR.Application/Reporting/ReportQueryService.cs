using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
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
        Guid requesterId, EmployeeRole role, DateOnly? date = null, CancellationToken ct = default);

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

    /// <summary>
    /// Payroll for the period on the fixed-monthly-salary model: each employee's salary, minus a
    /// per-day share for every unexcused absence. Built on top of <see cref="GetSummaryAsync"/>, so it
    /// shares the same scope authority and day-counting.
    /// </summary>
    Task<(ReportAccess Access, PayrollReport? Report)> GetPayrollAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default);

    /// <summary>
    /// The monthly timesheet grid ("Aylıq Tabel"): one row per in-scope employee, one code per day.
    /// Built on the same per-day computation as the summary, so it shares its scope authority and its
    /// day-counting — the tabel and the summary can never disagree about who worked when.
    /// </summary>
    Task<(ReportAccess Access, TabelReport? Report)> GetTabelAsync(
        int year, int month, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default);
}

public sealed class ReportQueryService : IReportQueryService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;
    private readonly string[] _hiddenEmails;

    public ReportQueryService(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _hiddenEmails = options.HiddenEmailList();
    }

    /// <summary>One employee-day's computed figures — the shape DailySummary persists, and the shape
    /// every reporting aggregate here consumes. A finished day is read from the table; today is
    /// computed into the same shape on demand, so the aggregates never need to know the difference.</summary>
    private sealed record DayRow(
        Guid EmployeeId,
        Guid LocationId,
        DateOnly Date,
        DateTime? CheckInAtUtc,
        DateTime? CheckOutAtUtc,
        DailySummaryStatus Status,
        int WorkedMinutes,
        int OvertimeMinutes,
        int LateMinutes);

    /// <summary>An in-scope employee plus the fields the day computation needs.</summary>
    private sealed record ScopedEmployee(
        Guid Id, string FullName, Guid LocationId, Guid? ScheduleId,
        TimeOnly? WorkStart, TimeOnly? WorkEnd,
        int? WorkCycleDays, int WorkCycleOnDays, DateOnly? WorkCycleAnchor);

    /// <summary>One employee's computed day with everything it was computed from still attached — so
    /// the two callers can each project what they need (the board wants the record's photo/face/reason
    /// fields; the reports want only the figures) without computing the day twice.</summary>
    private sealed record LiveDay(
        ScopedEmployee Employee, Location Location, AttendanceRecord? Record, DayComputation Computed);

    private DateOnly LocalToday() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

    /// <summary>
    /// The active employees this caller may see — the live-path twin of
    /// <see cref="LocationScope.ApplyLocationScopeAsync"/>, which narrows persisted summaries. Both
    /// must select the same people: a day computed live and a day read from DailySummary have to
    /// cover the same population, or the totals shift depending on which side of midnight you ask.
    /// </summary>
    private async Task<(ReportAccess Access, List<ScopedEmployee> Employees)> ScopedEmployeesAsync(
        Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct)
    {
        // Admins/managers who also clock in ARE included (e.g. a director who scans); only the
        // system/root accounts in HiddenEmails are left out. Same rule as the nightly job.
        var query = _db.Employees.Where(e =>
            e.IsActive && e.ActivatedAtUtc != null && !_hiddenEmails.Contains(e.Email.ToLower()));

        switch (role)
        {
            case EmployeeRole.Admin:
                if (locationId is Guid adminLoc)
                    query = query.Where(e => e.LocationId == adminLoc);
                break;

            case EmployeeRole.Manager:
                var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, requesterId, ct);
                if (locationId is Guid reqLoc)
                {
                    if (!managed.Contains(reqLoc))
                        return (ReportAccess.Forbidden, []);
                    query = query.Where(e => e.LocationId == reqLoc);
                }
                else
                {
                    query = query.Where(e => managed.Contains(e.LocationId));
                }
                break;

            default: // Employee — only themselves, whatever locationId was passed.
                query = query.Where(e => e.Id == requesterId);
                break;
        }

        var employees = await query
            .Select(e => new ScopedEmployee(
                e.Id, e.FullName, e.LocationId, e.ScheduleId, e.WorkStart, e.WorkEnd,
                e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor))
            .ToListAsync(ct);
        return (ReportAccess.Allowed, employees);
    }

    /// <summary>
    /// Computes one date for the given employees straight from raw AttendanceRecords — the same
    /// inputs and the same <see cref="AttendanceCalculator"/> the nightly job uses, so a day computed
    /// here and the row the job writes for it later agree.
    /// </summary>
    private async Task<List<LiveDay>> ComputeDayLiveAsync(
        DateOnly date, List<ScopedEmployee> employees, CancellationToken ct)
    {
        var rows = new List<LiveDay>(employees.Count);
        if (employees.Count == 0)
            return rows;

        var employeeIds = employees.Select(e => e.Id).ToList();
        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();

        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);
        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == date && employeeIds.Contains(r.EmployeeId))
            .ToDictionaryAsync(r => r.EmployeeId, ct);

        // A handful of rows per tenant; loaded whole and looked up in memory.
        var schedules = await _db.Schedules.ToDictionaryAsync(sc => sc.Id, ct);

        var nonWorkingLocationIds = await _db.NonWorkingDays
            .Where(n => n.Date == date && (n.LocationId == null || locationIds.Contains(n.LocationId.Value)))
            .Select(n => n.LocationId)
            .ToListAsync(ct);
        var isGloballyNonWorking = nonWorkingLocationIds.Contains(null);
        var nonWorkingLocationIdSet = nonWorkingLocationIds
            .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();

        var leaveByEmployee = await _db.LeaveRecords
            .Where(l => l.FromDate <= date && l.ToDate >= date && employeeIds.Contains(l.EmployeeId))
            .GroupBy(l => l.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Type = g.First().Type })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Type, ct);

        foreach (var e in employees)
        {
            if (!locations.TryGetValue(e.LocationId, out var location))
                continue; // defensive: the employee's location vanished

            var shift = EffectiveShift.Resolve(
                e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor,
                e.ScheduleId is Guid sid ? schedules.GetValueOrDefault(sid) : null, location);

            var isWorkingDay = shift.IsWorkingDay(date)
                               && !isGloballyNonWorking
                               && !nonWorkingLocationIdSet.Contains(location.Id);
            LeaveType? leaveType = leaveByEmployee.TryGetValue(e.Id, out var lt) ? lt : null;
            var noRecordStatus = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leaveType);

            records.TryGetValue(e.Id, out var record);
            // Judged against the same resolved shift the scan endpoint used.
            var c = AttendanceCalculator.Compute(record, shift, _timeZone, isWorkingDay, noRecordStatus);

            rows.Add(new LiveDay(e, location, record, c));
        }

        return rows;
    }

    private static DayRow ToDayRow(LiveDay d, DateOnly date) => new(
        d.Employee.Id, d.Employee.LocationId, date, d.Record?.CheckInAtUtc, d.Record?.CheckOutAtUtc,
        d.Computed.Status, d.Computed.WorkedMinutes, d.Computed.OvertimeMinutes, d.Computed.LateMinutes);

    /// <summary>
    /// The computed rows for [from..to], scoped to the caller.
    ///
    /// DailySummary is the record of a FINISHED day — the nightly job writes yesterday and never
    /// today. So today is computed live here, and any row that happens to exist for today in the
    /// table is ignored: several admin actions (an attendance edit, a leave entry, a non-working-day
    /// change) call GenerateForDateAsync with today's date and freeze a half-finished snapshot into
    /// it. The dashboard was reading that snapshot — showing, hours later, whatever the day looked
    /// like when an admin last touched it, or zeros where no admin had.
    /// </summary>
    private async Task<(ReportAccess Access, List<DayRow> Rows, string Label)> LoadDayRowsAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct)
    {
        var today = LocalToday();
        var rows = new List<DayRow>();

        // Finished days: straight from the table.
        var persistedTo = to < today ? to : today.AddDays(-1);
        var scoped = await LocationScope.ApplyLocationScopeAsync(
            _db, _db.DailySummaries.Where(s => s.SummaryDate >= from && s.SummaryDate <= persistedTo),
            requesterId, role, locationId, ct);
        if (scoped.Access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, rows, scoped.Label);

        if (persistedTo >= from)
        {
            rows.AddRange(await scoped.Query
                .Select(s => new DayRow(
                    s.EmployeeId, s.LocationId, s.SummaryDate, s.CheckInAtUtc, s.CheckOutAtUtc,
                    s.Status, s.WorkedMinutes, s.OvertimeMinutes, s.LateMinutes))
                .ToListAsync(ct));
        }

        // Today (only if the range actually reaches it): computed, never read.
        if (from <= today && today <= to)
        {
            var (access, employees) = await ScopedEmployeesAsync(locationId, requesterId, role, ct);
            if (access == ReportAccess.Forbidden)
                return (ReportAccess.Forbidden, [], "Forbidden");
            rows.AddRange((await ComputeDayLiveAsync(today, employees, ct)).Select(d => ToDayRow(d, today)));
        }

        return (ReportAccess.Allowed, rows, scoped.Label);
    }

    // The Azerbaijani T-13 codes the tabel prints. Kept here so the legend the UI shows and the codes
    // the grid fills always come from one place — a legend that disagrees with the cells is worse than
    // no legend.
    private const string CodeWorked = "İ";      // işlədi — turned up
    private const string CodeAbsent = "Q";      // qayıb — unexcused absence on a working day
    private const string CodeVacation = "M";    // məzuniyyət — annual leave
    private const string CodeSick = "X";        // xəstəlik
    private const string CodeUnpaid = "ÖM";     // ödənişsiz məzuniyyət
    private const string CodePermission = "İC"; // icazə — short excused absence
    private const string CodeHoliday = "B";     // bayram / admin-declared non-working day
    private const string CodeWeekend = "H";     // həftələrarası istirahət — off per the work-day mask
    private const string CodeFuture = "";       // a day that has not happened yet this month

    private static readonly IReadOnlyList<TabelLegendItem> TabelLegend = new[]
    {
        new TabelLegendItem(CodeWorked, "İşlədi"),
        new TabelLegendItem(CodeAbsent, "Qayıb (icazəsiz)"),
        new TabelLegendItem(CodeVacation, "Məzuniyyət"),
        new TabelLegendItem(CodeSick, "Xəstəlik"),
        new TabelLegendItem(CodeUnpaid, "Ödənişsiz məzuniyyət"),
        new TabelLegendItem(CodePermission, "İcazə"),
        new TabelLegendItem(CodeHoliday, "Bayram / qeyri-iş günü"),
        new TabelLegendItem(CodeWeekend, "İstirahət günü"),
    };

    public async Task<(ReportAccess Access, TabelReport? Report)> GetTabelAsync(
        int year, int month, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        if (month is < 1 or > 12)
            return (ReportAccess.Forbidden, null);

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, daysInMonth);
        var today = LocalToday();

        // The same computed day rows the summary uses — status + worked minutes per employee per day,
        // scoped to the caller. Reusing it is what keeps the tabel honest against every other report.
        var (access, dayRows, label) = await LoadDayRowsAsync(from, to, locationId, requesterId, role, ct);
        if (access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, null);

        // The roster itself, so an employee who was absent all month still gets a row — reports that
        // only list who showed up hide exactly the people a timesheet exists to catch.
        var (empAccess, employees) = await ScopedEmployeesAsync(locationId, requesterId, role, ct);
        if (empAccess == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, null);

        var employeeIds = employees.Select(e => e.Id).ToList();
        var meta = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Position, e.LocationId })
            .ToListAsync(ct);
        var positionById = meta.ToDictionary(m => m.Id, m => m.Position);
        var locationById = meta.ToDictionary(m => m.Id, m => m.LocationId);
        var allLocations = await _db.Locations.ToListAsync(ct);
        var locationNames = allLocations.ToDictionary(l => l.Id, l => l.Name);
        var locationEntityById = allLocations.ToDictionary(l => l.Id);
        var tabelSchedules = await _db.Schedules.ToDictionaryAsync(sc => sc.Id, ct);

        // DailySummaryStatus collapses every kind of approved leave into OnLeave; the tabel has to
        // tell M from X from ÖM, so the leave type comes straight from the LeaveRecords for the month.
        var leaves = await _db.LeaveRecords
            .Where(l => employeeIds.Contains(l.EmployeeId) && l.FromDate <= to && l.ToDate >= from)
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate, l.Type })
            .ToListAsync(ct);

        string LeaveCodeFor(Guid employeeId, DateOnly date)
        {
            var leave = leaves.FirstOrDefault(l => l.EmployeeId == employeeId && l.FromDate <= date && l.ToDate >= date);
            return leave?.Type switch
            {
                LeaveType.Vacation => CodeVacation,
                LeaveType.Sick => CodeSick,
                LeaveType.Unpaid => CodeUnpaid,
                LeaveType.Permission => CodePermission,
                _ => CodeVacation, // OnLeave with no matching row (shouldn't happen) — treat as leave, not absence
            };
        }

        // Fast lookup of the computed day per (employee, date).
        var byKey = dayRows.ToDictionary(r => (r.EmployeeId, r.Date));

        var rows = new List<TabelRow>(employees.Count);
        foreach (var e in employees.OrderBy(e => e.FullName))
        {
            var codes = new string[daysInMonth];
            int worked = 0, absent = 0, leave = 0, workedMinutes = 0;

            for (var day = 1; day <= daysInMonth; day++)
            {
                var date = new DateOnly(year, month, day);

                // A day that hasn't arrived yet is not an absence — the month isn't over.
                if (date > today)
                {
                    codes[day - 1] = CodeFuture;
                    continue;
                }

                string code;
                if (byKey.TryGetValue((e.Id, date), out var d))
                {
                    code = d.Status switch
                    {
                        // Any activity is worked time, including a missing check-out — that is a
                        // check-out problem for another screen, not an absence here.
                        DailySummaryStatus.OnTime or DailySummaryStatus.Late or DailySummaryStatus.Incomplete => CodeWorked,
                        DailySummaryStatus.Absent => CodeAbsent,
                        DailySummaryStatus.OnLeave => LeaveCodeFor(e.Id, date),
                        DailySummaryStatus.Permission => CodePermission,
                        DailySummaryStatus.DayOff => CodeWeekend,
                        _ => CodeAbsent,
                    };
                    workedMinutes += d.WorkedMinutes;
                }
                else
                {
                    // No computed row (e.g. an employee added mid-month): fall back to the calendar —
                    // a work day with no record is absent, a non-work day is rest. Resolved through
                    // the same shift rule as everything else, so a rotation still reads correctly here.
                    var loc = locationEntityById.GetValueOrDefault(locationById.GetValueOrDefault(e.Id));
                    code = loc is null
                        ? CodeAbsent
                        : EffectiveShift.Resolve(
                              e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor,
                              e.ScheduleId is Guid sid2 ? tabelSchedules.GetValueOrDefault(sid2) : null, loc)
                          .IsWorkingDay(date) ? CodeAbsent : CodeWeekend;
                }

                codes[day - 1] = code;
                if (code == CodeWorked) worked++;
                else if (code == CodeAbsent) absent++;
                else if (code is CodeVacation or CodeSick or CodeUnpaid or CodePermission) leave++;
            }

            // Admin-declared holidays turn a weekend/absent cell into B. Applied last so it wins over
            // the calendar but never over a real check-in.
            rows.Add(new TabelRow(
                e.Id, e.FullName,
                positionById.GetValueOrDefault(e.Id),
                locationNames.GetValueOrDefault(e.LocationId, ""),
                codes, worked, absent, leave, Math.Round(workedMinutes / 60.0, 1)));
        }

        // Overlay admin-declared non-working days (bayram) as B, on the cells that are otherwise empty
        // rest days — done once for the whole grid rather than per employee-day.
        await ApplyHolidaysAsync(rows, employees, year, month, daysInMonth, today, ct);

        return (ReportAccess.Allowed, new TabelReport(year, month, label, daysInMonth, rows, TabelLegend));
    }

    /// <summary>Marks admin-declared non-working days as B (bayram) across the grid, on rest cells
    /// only — a holiday someone still came in for stays İ.</summary>
    private async Task ApplyHolidaysAsync(
        List<TabelRow> rows, List<ScopedEmployee> employees, int year, int month, int daysInMonth,
        DateOnly today, CancellationToken ct)
    {
        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, daysInMonth);
        var holidays = await _db.NonWorkingDays
            .Where(n => n.Date >= from && n.Date <= to)
            .Select(n => new { n.Date, n.LocationId })
            .ToListAsync(ct);
        if (holidays.Count == 0) return;

        var locationByEmployee = employees.ToDictionary(e => e.Id, e => e.LocationId);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var empLoc = locationByEmployee.GetValueOrDefault(row.EmployeeId);
            var codes = row.Days.ToArray();
            foreach (var h in holidays)
            {
                if (h.Date > today) continue;
                if (h.LocationId is Guid hl && hl != empLoc) continue; // location-specific holiday
                var idx = h.Date.Day - 1;
                // Only recolour a rest cell → holiday. Never touch worked time, leave, or an absence:
                // recolouring an absence would silently drop it from the AbsentDays total computed above.
                if (codes[idx] == CodeWeekend)
                    codes[idx] = CodeHoliday;
            }
            rows[i] = row with { Days = codes };
        }
    }

    public async Task<(ReportAccess Access, AttendanceReport? Report)> GetSummaryAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // Same scope authority for JSON and Excel — export cannot bypass it. Today is computed live
        // rather than read from DailySummary; see LoadDayRowsAsync.
        var (access, dayRows, label) = await LoadDayRowsAsync(from, to, locationId, requesterId, role, ct);
        if (access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, null);

        var (employeeNames, locationNames) = await NamesForAsync(dayRows, ct);
        var rows = dayRows
            .Where(r => employeeNames.ContainsKey(r.EmployeeId))
            .Select(r => new
            {
                r.EmployeeId,
                FullName = employeeNames[r.EmployeeId],
                // The row's OWN location, not the employee's current one: a day belongs to wherever
                // they were working that day, and this report is history.
                LocationName = locationNames.GetValueOrDefault(r.LocationId, string.Empty),
                r.Status,
                r.WorkedMinutes,
                r.OvertimeMinutes,
            })
            .ToList();

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

        return (ReportAccess.Allowed, new AttendanceReport(from, to, label, grouped, totals));
    }

    public async Task<(ReportAccess Access, PayrollReport? Report)> GetPayrollAsync(
        DateOnly from, DateOnly to, Guid? locationId, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // Reuse the summary — same scope check, same day-counting — then price it. Nothing here can see
        // a wider scope than GetSummaryAsync already allowed.
        var (access, summary) = await GetSummaryAsync(from, to, locationId, requesterId, role, ct);
        if (access == ReportAccess.Forbidden || summary is null)
            return (ReportAccess.Forbidden, null);

        var ids = summary.Rows.Select(r => r.EmployeeId).ToList();
        var salaries = await _db.Employees
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.MonthlySalary, ct);

        var rows = summary.Rows.Select(r =>
        {
            var salary = salaries.GetValueOrDefault(r.EmployeeId);
            // The divisor is every day that was a working day for this employee in the period —
            // present, absent, or excused (leave/permission). Only unexcused absences are deducted.
            var scheduled = r.WorkDays + r.AbsentDays + r.LeaveDays + r.PermissionDays;

            decimal perDay = 0m, deduction = 0m, payable = 0m;
            if (salary is > 0m)
            {
                if (scheduled > 0)
                {
                    perDay = Math.Round(salary.Value / scheduled, 2, MidpointRounding.AwayFromZero);
                    deduction = Math.Round(perDay * r.AbsentDays, 2, MidpointRounding.AwayFromZero);
                    payable = salary.Value - deduction;
                    if (payable < 0m) payable = 0m;
                }
                else
                {
                    // Salary set but no working day fell in the period (all day-off) — nothing to deduct.
                    payable = salary.Value;
                }
            }

            return new PayrollRow(
                r.EmployeeId, r.EmployeeName, r.LocationName, salary,
                scheduled, r.WorkDays, r.AbsentDays, r.LeaveDays, r.PermissionDays, r.OvertimeHours,
                perDay, deduction, payable);
        })
        .OrderBy(r => r.EmployeeName)
        .ToList();

        var report = new PayrollReport(
            from, to, summary.ScopeLabel, rows,
            TotalMonthlySalary: rows.Sum(r => r.MonthlySalary ?? 0m),
            TotalDeduction: rows.Sum(r => r.Deduction),
            TotalPayable: rows.Sum(r => r.Payable));

        return (ReportAccess.Allowed, report);
    }

    /// <summary>Employee and location names for a set of computed rows. The rows carry ids only:
    /// they come from two sources (the summary table and a live computation) and only one of those
    /// could join.</summary>
    private async Task<(Dictionary<Guid, string> Employees, Dictionary<Guid, string> Locations)> NamesForAsync(
        List<DayRow> rows, CancellationToken ct)
    {
        var employeeIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var locationIds = rows.Select(r => r.LocationId).Distinct().ToList();

        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var employees = await _db.Employees
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        return (employees, locations);
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
        Guid requesterId, EmployeeRole role, DateOnly? date = null, CancellationToken ct = default)
    {
        // Defaults to the local "today"; a past date shows that day's board (same computation, so the
        // live board and any historical day read identically).
        var day = date ?? LocalToday();

        // No location filter here: the board has its own client-side one, and a Manager is already
        // narrowed to their locations by scope.
        var (access, employees) = await ScopedEmployeesAsync(null, requesterId, role, ct);
        if (access == ReportAccess.Forbidden)
            return [];

        var computed = await ComputeDayLiveAsync(day, employees, ct);

        // A night shift is still running when the calendar turns over. Records are keyed by the day
        // the shift STARTED, so at 01:00 someone who scanned in at 21:00 has a record dated yesterday
        // and simply vanished from "today" — the board showed them as absent while they were at work.
        // Only for the live board: a past day must stay exactly what it was.
        if (date is null || date == LocalToday())
            computed = await CarryOverOpenShiftsAsync(day, employees, computed, ct);

        return computed
            .Select(d => new DayAttendanceRow(
                d.Employee.Id, d.Employee.FullName, d.Location.Id, d.Location.Name, d.Computed.Status.ToString(),
                d.Record?.CheckInAtUtc, d.Record?.CheckOutAtUtc,
                d.Record?.Id, d.Record?.CheckInPhotoKey != null,
                d.Record?.FaceMatchScore, d.Record?.FaceMatchStatus.ToString() ?? "NotChecked",
                d.Record?.LateArrivalReason, d.Record?.EarlyDepartureReason,
                d.Record?.WasOffline ?? false))
            .OrderBy(r => r.EmployeeName)
            .ToList();
    }

    /// <summary>
    /// Replaces "absent today" with the shift they are actually still on.
    ///
    /// Someone checked in and not yet out is at work right now, whatever date their record carries —
    /// so for anyone with nothing today, yesterday's row is used instead if it is still open. Applied
    /// to the live board only; a historical day must not borrow from its neighbour.
    /// </summary>
    private async Task<List<LiveDay>> CarryOverOpenShiftsAsync(
        DateOnly day, List<ScopedEmployee> employees, List<LiveDay> computed, CancellationToken ct)
    {
        var withoutToday = computed
            .Where(d => d.Record?.CheckInAtUtc is null)
            .Select(d => d.Employee)
            .ToList();
        if (withoutToday.Count == 0)
            return computed;

        var yesterday = await ComputeDayLiveAsync(day.AddDays(-1), withoutToday, ct);
        var stillOpen = yesterday
            .Where(d => d.Record?.CheckInAtUtc is not null && d.Record.CheckOutAtUtc is null)
            .ToDictionary(d => d.Employee.Id);
        if (stillOpen.Count == 0)
            return computed;

        return computed
            .Select(d => stillOpen.TryGetValue(d.Employee.Id, out var open) ? open : d)
            .ToList();
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
        // Same scope authority as GetSummaryAsync — one rule for every reporting view. Today is
        // computed live rather than read from DailySummary; see LoadDayRowsAsync.
        var (access, summaries, label) = await LoadDayRowsAsync(from, to, locationId, requesterId, role, ct);
        if (access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, null);

        // Everything below is scoped to exactly these employees — derived from the same
        // already-scoped rows, rather than re-deriving scope rules a second time.
        var scopedEmployeeIds = summaries.Select(s => s.EmployeeId).Distinct().ToList();

        var totalCheckIns = summaries.Count(s => s.CheckInAtUtc != null);
        var totalCheckOuts = summaries.Count(s => s.CheckOutAtUtc != null);
        var lateCount = summaries.Count(s => s.Status == DailySummaryStatus.Late);
        var absentCount = summaries.Count(s => s.Status == DailySummaryStatus.Absent);

        // Incomplete means "checked in, never out". On a finished day that is a forgotten check-out —
        // the day reads as zero hours until an admin closes it, so it needs attention. On TODAY it
        // just means the person is still at work, which needs nothing. The dashboard used to lump
        // them together and reported everyone currently on shift as having forgotten to leave.
        var todayLocal = LocalToday();
        var incompleteCount = summaries.Count(s => s.Status == DailySummaryStatus.Incomplete && s.Date != todayLocal);
        var stillAtWorkCount = summaries.Count(s => s.Status == DailySummaryStatus.Incomplete && s.Date == todayLocal);
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

        // Employees WITH a device, not rows: one person can now hold several bindings (Safari, the
        // installed PWA), and the dashboard tile means "how many staff can scan".
        var activeDeviceCount = await _db.DeviceBindings
            .Where(d => d.IsActive && scopedEmployeeIds.Contains(d.EmployeeId))
            .Select(d => d.EmployeeId)
            .Distinct()
            .CountAsync(ct);

        var trend = summaries
            .GroupBy(s => s.Date)
            .Select(g => new DailyTrendPoint(g.Key, g.Count(x => x.CheckInAtUtc != null), g.Count(x => x.CheckOutAtUtc != null)))
            .OrderBy(p => p.Date)
            .ToList();

        var weekday = summaries
            .GroupBy(s => (int)s.Date.DayOfWeek)
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
            from, to, label,
            totalCheckIns, totalCheckOuts, lateCount, absentCount, incompleteCount, stillAtWorkCount,
            dayOffCount, leaveCount, permissionCount,
            totalWorkedHours, overtimeHours, outsideRadiusCount, activeDeviceCount,
            checkInOutRatio, lateRate, outsideRadiusRate, avgDailyOperations,
            trend, weekday, topLate);

        return (ReportAccess.Allowed, report);
    }
}
