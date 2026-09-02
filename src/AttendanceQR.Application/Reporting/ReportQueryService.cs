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

    /// <summary>One employee's day-by-day rows for [from..to] — the breakdown behind the profile
    /// summary tiles. Same scope authority as the reports (a manager only their own staff).</summary>
    Task<(ReportAccess Access, IReadOnlyList<EmployeeDayRow> Days)> GetEmployeeDaysAsync(
        Guid employeeId, DateOnly from, DateOnly to, Guid requesterId, EmployeeRole role, CancellationToken ct = default);

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
        DateOnly from, DateOnly to, Guid requesterId, EmployeeRole role, CancellationToken ct = default);

    /// <summary>
    /// People whose real arrival times disagree with the shift they are assigned to — see
    /// <see cref="ShiftFit"/>. Same scope authority as every other report.
    /// </summary>
    Task<(ReportAccess Access, ShiftMismatchReport? Report)> GetShiftMismatchAsync(
        int days, Guid requesterId, EmployeeRole role, CancellationToken ct = default);

    /// <summary>People whose phones are refusing to scan (GPS/camera family) with no success since.</summary>
    Task<(ReportAccess Access, IReadOnlyList<StuckDeviceRow> Rows)> GetStuckDevicesAsync(
        Guid requesterId, EmployeeRole role, CancellationToken ct = default);

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
        int LateMinutes,
        int EarlyLeaveMinutes = 0,
        int EarlyArriveMinutes = 0);

    /// <summary>An in-scope employee plus the fields the day computation needs.</summary>
    private sealed record ScopedEmployee(
        Guid Id, string FullName, Guid LocationId, Guid? ScheduleId,
        TimeOnly? WorkStart, TimeOnly? WorkEnd,
        int? WorkCycleDays, int WorkCycleOnDays, DateOnly? WorkCycleAnchor,
        // Carried so the board can show this person's selfie on every scan, not only a flagged one:
        // once their account may ride on a shared phone, the face is the control that remains.
        bool CanShareDevice = false,
        // The job title, so the board can show it and be filtered by it — «bütün bağbanlar» is a
        // question somebody asks of a morning, and the answer was one the screen could not give.
        string? Position = null);

    /// <summary>One employee's computed day with everything it was computed from still attached — so
    /// the two callers can each project what they need (the board wants the record's photo/face/reason
    /// fields; the reports want only the figures) without computing the day twice.</summary>
    private sealed record LiveDay(
        ScopedEmployee Employee, Location Location, AttendanceRecord? Record, DayComputation Computed,
        EffectiveShift Shift, LeaveType? Leave, string? LeaveAssignedBy = null, Guid? LeaveId = null,
        string? ManualBy = null,
        // Field/mobile attendance for the same day, when there is any: earliest arrival, and a
        // departure only once EVERY visit that day is closed (otherwise they are still on site).
        // Deliberately kept OUT of Record: the board tells a field day apart precisely by the absence
        // of an office record, and Record also carries scan-only things (photo, face match, id).
        DateTime? FieldIn = null, DateTime? FieldOut = null,
        double? FieldLat = null, double? FieldLng = null);

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
        // For an ADMIN, admins/managers who also clock in ARE included (e.g. a director who scans);
        // only the system/root accounts in HiddenEmails are left out. Same rule as the nightly job.
        // A MANAGER sees everyone at the branches they manage, including other managers and an admin
        // who clocks in there. It used to stop at Role==Employee, which made a two-manager site report
        // a headcount short by one with nothing on screen to explain it. Acting on those people is a
        // separate question and still refused — see LocationScopeRules.CanManageEmployeeAsync.
        var query = _db.Employees.Where(e =>
            e.IsActive && e.ActivatedAtUtc != null && (e.Email == null || !_hiddenEmails.Contains(e.Email.ToLower())));

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
                    query = query.Where(e => managed.Contains(e.LocationId) || e.Id == requesterId);
                }
                break;

            default: // Employee — only themselves, whatever locationId was passed.
                query = query.Where(e => e.Id == requesterId);
                break;
        }

        var employees = await query
            .Select(e => new ScopedEmployee(
                e.Id, e.FullName, e.LocationId, e.ScheduleId, e.WorkStart, e.WorkEnd,
                e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor, e.CanShareDevice, e.Position))
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

        // Field visits for the same day. Loaded HERE, once, because every consumer of this method
        // needs them: the board labels such a day "Sahədə", and the reports/tabel must count it as
        // worked (a day in the field is work, and the payroll deducts for anything that reads Qayıb).
        // The nightly job applies the identical rule in DailySummaryService, so a day computed live
        // and the row written for it tonight agree.
        var fieldRaw = await _db.FieldVisits
            .Where(v => v.VisitDate == date && employeeIds.Contains(v.EmployeeId)
                        && v.Status != FieldVisitStatus.Cancelled && v.CheckInAtUtc != null)
            .Select(v => new { v.EmployeeId, v.CheckInAtUtc, v.CheckOutAtUtc, v.CheckInLatitude, v.CheckInLongitude })
            .ToListAsync(ct);
        var fieldByEmployee = fieldRaw
            .GroupBy(v => v.EmployeeId)
            .ToDictionary(g => g.Key, g =>
            {
                var first = g.OrderBy(x => x.CheckInAtUtc).First();
                return (
                    In: first.CheckInAtUtc,
                    Out: g.All(x => x.CheckOutAtUtc != null) ? g.Max(x => x.CheckOutAtUtc) : (DateTime?)null,
                    Lat: first.CheckInLatitude,
                    Lng: first.CheckInLongitude,
                    // The visits as individual stretches, not just their outer bounds — two short
                    // visits hours apart must not be measured as one long one.
                    Spans: g.Where(x => x.CheckInAtUtc != null && x.CheckOutAtUtc != null)
                        .Select(x => new AttendanceCalculator.WorkSpan(x.CheckInAtUtc!.Value, x.CheckOutAtUtc!.Value))
                        .ToList(),
                    AnyOpen: g.Any(x => x.CheckOutAtUtc == null));
            });

        // A handful of rows per tenant; loaded whole and looked up in memory.
        var schedules = await _db.Schedules.ToDictionaryAsync(sc => sc.Id, ct);

        var nonWorkingLocationIds = await _db.NonWorkingDays
            .Where(n => n.Date == date && (n.LocationId == null || locationIds.Contains(n.LocationId.Value)))
            .Select(n => n.LocationId)
            .ToListAsync(ct);
        var isGloballyNonWorking = nonWorkingLocationIds.Contains(null);
        var nonWorkingLocationIdSet = nonWorkingLocationIds
            .Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();

        var leaveRows = await _db.LeaveRecords
            .Where(l => l.FromDate <= date && l.ToDate >= date && employeeIds.Contains(l.EmployeeId))
            .Select(l => new { l.Id, l.EmployeeId, l.Type, l.CreatedByEmployeeId, l.FromDate, l.ToDate })
            .ToListAsync(ct);
        var leaveByEmployee = leaveRows
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());
        // Who pinned each leave — surfaced on the board so an assigned reason is attributable to the
        // admin/manager who set it, not an anonymous status flip.
        var creatorIds = leaveRows.Select(l => l.CreatedByEmployeeId).Distinct().ToList();
        var creatorNames = await _db.Employees
            .Where(e => creatorIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName })
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        // Who set an attendance record by hand (open-record close, time fix, undo-checkout) — resolved
        // once so the board can flag a manually-entered day the same way it names a leave's assigner.
        var manualByIds = records.Values
            .Where(r => r.ManualByEmployeeId != null).Select(r => r.ManualByEmployeeId!.Value).Distinct().ToList();
        var manualByNames = manualByIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Employees.Where(e => manualByIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

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
            LeaveType? leaveType = null;
            string? leaveAssignedBy = null;
            Guid? leaveId = null;
            if (leaveByEmployee.TryGetValue(e.Id, out var lr))
            {
                leaveType = lr.Type;
                leaveAssignedBy = creatorNames.GetValueOrDefault(lr.CreatedByEmployeeId);
                // Only a single-day leave is undoable from the board — deleting a multi-day vacation
                // here would silently wipe its other days. Multi-day leaves are managed in /admin/leaves.
                leaveId = lr.FromDate == lr.ToDate ? lr.Id : null;
            }
            var noRecordStatus = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leaveType);

            records.TryGetValue(e.Id, out var record);
            // Judged against the same resolved shift the scan endpoint used.
            var c = AttendanceCalculator.Compute(record, shift, _timeZone, isWorkingDay, noRecordStatus);

            // No office scan but they were in the field → the day is worked, not Qayıb. An office scan
            // always wins (folding field minutes into it would double-count overlapping time), and this
            // overrides DayOff/OnLeave for the same reason a scan on a leave day does: turning up is
            // worked time. Lateness is never invented — a field arrival has no fixed hour to miss.
            fieldByEmployee.TryGetValue(e.Id, out var fv);
            // A field day with no office scan is WORKED, not Qayıb — and it overrides DayOff/OnLeave
            // for the same reason a scan on a leave day does: turning up is worked time. Lateness is
            // never invented; a field arrival has no fixed hour to miss.
            if (fv.In is not null && record?.CheckInAtUtc is null)
            {
                c = fv.AnyOpen
                    ? new DayComputation(DailySummaryStatus.Incomplete, 0, 0, 0)
                    : new DayComputation(DailySummaryStatus.OnTime, 0, 0, 0);
            }

            // Minutes across BOTH halves — the same shared decision the nightly job makes, so the
            // board and the stored summary can never disagree. Status, lateness and overtime stay as
            // resolved above: only the office half has an hour it was due at.
            var merged = AttendanceCalculator.MergedWorkedMinutes(record, fv.Spans ?? new List<AttendanceCalculator.WorkSpan>(), fv.AnyOpen);
            if (merged is int minutes)
                c = c with { WorkedMinutes = minutes };

            var manualBy = record?.ManualByEmployeeId is Guid mby ? manualByNames.GetValueOrDefault(mby) : null;
            rows.Add(new LiveDay(e, location, record, c, shift, leaveType, leaveAssignedBy, leaveId, manualBy,
                fv.In, fv.Out, fv.Lat, fv.Lng));
        }

        return rows;
    }

    // Times fall back to the field visit's, so a day worked in the field shows real hours in the
    // reports instead of an empty "—" beside a worked status.
    private static DayRow ToDayRow(LiveDay d, DateOnly date) => new(
        d.Employee.Id, d.Employee.LocationId, date,
        d.Record?.CheckInAtUtc ?? d.FieldIn, d.Record?.CheckOutAtUtc ?? d.FieldOut,
        d.Computed.Status, d.Computed.WorkedMinutes, d.Computed.OvertimeMinutes, d.Computed.LateMinutes,
        d.Computed.EarlyLeaveMinutes, d.Computed.EarlyArriveMinutes);

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
                    s.Status, s.WorkedMinutes, s.OvertimeMinutes, s.LateMinutes, s.EarlyLeaveMinutes, s.EarlyArriveMinutes))
                .ToListAsync(ct));
        }

        // Today (only if the range actually reaches it): computed, never read.
        if (from <= today && today <= to)
        {
            var (access, employees) = await ScopedEmployeesAsync(locationId, requesterId, role, ct);
            if (access == ReportAccess.Forbidden)
                return (ReportAccess.Forbidden, [], "Forbidden");

            // Somebody still being set up is dropped from the REPORTS for today, exactly as the
            // nightly job will drop them from the stored row tonight — otherwise a report covering
            // today would count an onboarding day as Qayıb and then quietly stop counting it at
            // midnight. Deliberately filtered HERE and not inside ComputeDayLiveAsync: the live
            // board shares that method and must keep showing these people, because "hasn't scanned
            // yet" is exactly what an admin needs to see today. The board reports presence; this
            // path feeds the summary, the tabel and the payroll, which decide money.
            var isOnboarding = await OnboardingCheckerAsync(employees.Select(e => e.Id).ToList(), ct);
            rows.AddRange((await ComputeDayLiveAsync(today, employees, ct))
                .Where(d => !isOnboarding(d.Employee.Id, today))
                .Select(d => ToDayRow(d, today)));
        }

        return (ReportAccess.Allowed, rows, scoped.Label);
    }

    /// <summary>
    /// Loads once, answers many: is this employee still being set up on that date? — see
    /// <see cref="AttendanceCalculator.IsStillOnboarding"/> for the rule and why it exists. Returned
    /// as a closure because the tabel asks the question per employee-day across a whole month, and
    /// thirty per-date queries would be thirty times the same three lookups.
    /// </summary>
    private async Task<Func<Guid, DateOnly, bool>> OnboardingCheckerAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var activated = await _db.Employees
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.ActivatedAtUtc })
            .ToDictionaryAsync(x => x.Id, x => x.ActivatedAtUtc, ct);

        var firstScan = await _db.AttendanceRecords
            .Where(r => ids.Contains(r.EmployeeId) && r.CheckInAtUtc != null)
            .GroupBy(r => r.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, First = g.Min(r => r.AttendanceDate) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.First, ct);

        var firstField = await _db.FieldVisits
            .Where(v => ids.Contains(v.EmployeeId) && v.CheckInAtUtc != null
                        && v.Status != FieldVisitStatus.Cancelled)
            .GroupBy(v => v.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, First = g.Min(v => v.VisitDate) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.First, ct);

        return (employeeId, date) =>
        {
            var a = firstScan.TryGetValue(employeeId, out var s1) ? s1 : (DateOnly?)null;
            var b = firstField.TryGetValue(employeeId, out var s2) ? s2 : (DateOnly?)null;
            var first = a is null ? b : b is null ? a : (a < b ? a : b);
            return AttendanceCalculator.IsStillOnboarding(
                date, activated.GetValueOrDefault(employeeId), first, _timeZone);
        };
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
    private const string CodeBusinessTrip = "Ez"; // ezamiyyət — work trip, no scan but working & paid
    private const string CodeHoliday = "B";     // bayram / admin-declared non-working day
    private const string CodeWeekend = "H";     // həftələrarası istirahət — off per the work-day mask
    private const string CodeFuture = "";       // a day that has not happened yet this month
    // İmport olunub, hələ ilk skanı yoxdur (bax IsStillOnboarding). Qayıb DEYİL: telefon hələ
    // paylanmayıb — bu günlər tabeldə də, maaşda da heç kimin əleyhinə işləmir.
    private const string CodeNotActivated = "–";

    private static readonly IReadOnlyList<TabelLegendItem> TabelLegend = new[]
    {
        new TabelLegendItem(CodeWorked, "İşlədi"),
        new TabelLegendItem(CodeAbsent, "Qayıb (icazəsiz)"),
        new TabelLegendItem(CodeVacation, "Məzuniyyət"),
        new TabelLegendItem(CodeSick, "Xəstəlik"),
        new TabelLegendItem(CodeUnpaid, "Ödənişsiz məzuniyyət"),
        new TabelLegendItem(CodePermission, "İcazə"),
        new TabelLegendItem(CodeBusinessTrip, "Ezamiyyət"),
        new TabelLegendItem(CodeHoliday, "Bayram / qeyri-iş günü"),
        new TabelLegendItem(CodeWeekend, "İstirahət günü"),
        new TabelLegendItem(CodeNotActivated, "Aktivləşdirməyib (ilk skana qədər)"),
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
        var isOnboarding = await OnboardingCheckerAsync(employeeIds, ct);
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
                LeaveType.BusinessTrip => CodeBusinessTrip,
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

                // Still being set up: the summary rows for these days were deliberately never written
                // (or were removed), but the fallback below re-derives Q from the CALENDAR — "a work
                // day with no record is absent" — so without this guard the tabel resurrects every
                // absence the onboarding rule just cleared. Checked before both branches for the same
                // reason, and it does not touch the absent counter.
                if (isOnboarding(e.Id, date))
                {
                    codes[day - 1] = CodeNotActivated;
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
                else if (code is CodeVacation or CodeSick or CodeUnpaid or CodePermission or CodeBusinessTrip) leave++;
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
                // Carried so an Ezamiyyət day can be told from an annual-leave one below; both arrive
                // here as OnLeave and the status alone cannot separate them.
                r.Date,
                r.WorkedMinutes,
                r.OvertimeMinutes,
                r.EarlyLeaveMinutes,
                r.EarlyArriveMinutes,
            })
            .ToList();

        // Which of those OnLeave days were Ezamiyyət. DailySummary stores only the status, so the
        // type comes from LeaveRecords — the same source and the same shape as the tabel's
        // LeaveCodeFor and the profile's LeaveTypeFor, rather than threading a field through the
        // shared DayRow that the persisted path cannot fill.
        var tripDays = await _db.LeaveRecords
            .Where(l => l.Type == LeaveType.BusinessTrip && l.FromDate <= to && l.ToDate >= from)
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate })
            .ToListAsync(ct);
        bool OnTrip(Guid employeeId, DateOnly date) =>
            tripDays.Any(l => l.EmployeeId == employeeId && l.FromDate <= date && l.ToDate >= date);

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
                EarlyLeaveHours: Math.Round(g.Sum(x => x.EarlyLeaveMinutes) / 60.0, 2),
                EarlyArriveHours: Math.Round(g.Sum(x => x.EarlyArriveMinutes) / 60.0, 2),
                LeaveDays: g.Count(x => x.Status == DailySummaryStatus.OnLeave && !OnTrip(x.EmployeeId, x.Date)),
                TripDays: g.Count(x => x.Status == DailySummaryStatus.OnLeave && OnTrip(x.EmployeeId, x.Date)),
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
            EarlyLeaveHours: Math.Round(grouped.Sum(r => r.EarlyLeaveHours), 2),
            EarlyArriveHours: Math.Round(grouped.Sum(r => r.EarlyArriveHours), 2),
            LeaveDays: grouped.Sum(r => r.LeaveDays),
            TripDays: grouped.Sum(r => r.TripDays),
            PermissionDays: grouped.Sum(r => r.PermissionDays));

        return (ReportAccess.Allowed, new AttendanceReport(from, to, label, grouped, totals));
    }

    public async Task<(ReportAccess Access, IReadOnlyList<EmployeeDayRow> Days)> GetEmployeeDaysAsync(
        Guid employeeId, DateOnly from, DateOnly to, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // Same rows the summary is built from, filtered to one person. LoadDayRowsAsync applies the
        // caller's scope, so a manager asking for someone outside their branches simply gets nothing.
        var (access, dayRows, _) = await LoadDayRowsAsync(from, to, null, requesterId, role, ct);
        if (access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, Array.Empty<EmployeeDayRow>());

        // The day rows collapse every leave into OnLeave; the profile breakdown wants to say WHICH
        // (Xəstəlik vs Ezamiyyət vs Məzuniyyət). Resolve it from LeaveRecords here — the same source and
        // approach the tabel's LeaveCodeFor uses — instead of threading a field through the shared DayRow
        // (which the persisted path reads from DailySummaries, where the leave type isn't stored).
        var leaves = await _db.LeaveRecords
            .Where(l => l.EmployeeId == employeeId && l.FromDate <= to && l.ToDate >= from)
            .Select(l => new { l.FromDate, l.ToDate, l.Type })
            .ToListAsync(ct);
        string? LeaveTypeFor(DateOnly date) =>
            leaves.FirstOrDefault(l => l.FromDate <= date && l.ToDate >= date)?.Type.ToString();

        var days = dayRows
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.Date)
            .Select(r => new EmployeeDayRow(
                r.Date, r.Status.ToString(), r.CheckInAtUtc, r.CheckOutAtUtc, r.WorkedMinutes, r.LateMinutes,
                r.Status == DailySummaryStatus.OnLeave ? LeaveTypeFor(r.Date) : null))
            .ToList();
        return (ReportAccess.Allowed, days);
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

        // A MANAGER sees what their own staff are owed — and not what their peers or the admin at the
        // same branch are paid. The scope this is built on deliberately carries every role (it feeds
        // the boards, where a headcount that quietly omitted the managers read short), so the ceiling
        // has to be applied here rather than by narrowing the shared helper.
        //
        // The controller's comment claimed this already happened. It did not: the scope has no role
        // filter, so a manager's payroll table listed a peer manager's and a same-branch admin's
        // salary. One pay figure is the most sensitive number in this product.
        if (role == EmployeeRole.Manager)
        {
            var ownStaff = await _db.Employees
                .Where(e => ids.Contains(e.Id) && (e.Role == EmployeeRole.Employee || e.Id == requesterId))
                .Select(e => e.Id)
                .ToListAsync(ct);
            var allowed = ownStaff.ToHashSet();
            summary = summary with { Rows = summary.Rows.Where(r => allowed.Contains(r.EmployeeId)).ToList() };
            ids = summary.Rows.Select(r => r.EmployeeId).ToList();
        }

        var salaries = await _db.Employees
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.MonthlySalary, ct);

        var rows = summary.Rows.Select(r =>
        {
            var salary = salaries.GetValueOrDefault(r.EmployeeId);
            // The arithmetic lives in PayrollMath — tested, and with the trip-day story on it. It was
            // inline here, and when Ezamiyyət was split out of LeaveDays the divisor silently lost
            // those days: every trip inflated the per-day rate and over-charged any absence.
            var scheduled = 0;
            decimal perDay = 0m, deduction = 0m, payable = 0m;
            if (salary is > 0m)
                (scheduled, perDay, deduction, payable) = PayrollMath.Compute(
                    salary.Value, r.WorkDays, r.AbsentDays, r.LeaveDays, r.PermissionDays, r.TripDays);
            else
                scheduled = r.WorkDays + r.AbsentDays + r.LeaveDays + r.PermissionDays + r.TripDays;

            return new PayrollRow(
                r.EmployeeId, r.EmployeeName, r.LocationName, salary,
                scheduled, r.WorkDays, r.AbsentDays, r.LeaveDays, r.PermissionDays, r.OvertimeHours,
                perDay, deduction, payable, r.EarlyLeaveHours, r.EarlyArriveHours);
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
        var isToday = date is null || date == LocalToday();
        if (isToday)
            computed = await CarryOverOpenShiftsAsync(day, employees, computed, ct);

        var nowLocal = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        // Who is still being set up (imported, no first scan yet — see IsStillOnboarding). Their
        // "Absent" is relabelled "Onboarding" for the BOARD ONLY, the same way "Pending" and "Field"
        // are board-only strings the stored enum never carries. Why it matters at this scale: the
        // morning 290 people were imported, the Qayıb tile read ~350 and the 67 genuinely missing
        // people were invisible inside the noise. The admin still sees every one of them — under an
        // honest name, on their own tile — and the Qayıb count means something again.
        var isOnboarding = await OnboardingCheckerAsync(employees.Select(e => e.Id).ToList(), ct);

        // Field/mobile attendance rides along on LiveDay — ComputeDayLiveAsync loaded it once, and has
        // already counted such a day as worked so the reports and the payroll agree with this board.
        return computed
            .Select(d =>
            {
                var status = BoardDisplayStatus(d.Computed.Status, d.Shift, isToday, nowLocal, day);
                // A field check-in with no office record → "Sahədə". Checked before the status is read
                // for anything else, because ComputeDayLiveAsync now scores such a day as worked, so
                // it arrives here as OnTime/Incomplete rather than Absent/Pending.
                if (d.FieldIn != null && d.Record?.CheckInAtUtc == null)
                    status = "Field";
                // Only the Absent reading is overridden: a scan ends onboarding by definition, and
                // Pending ("shift not started yet") is already neutral.
                if (status == "Absent" && isOnboarding(d.Employee.Id, day))
                    status = "Onboarding";
                return new DayAttendanceRow(
                    d.Employee.Id, d.Employee.FullName, d.Location.Id, d.Location.Name,
                    status,
                    d.Record?.CheckInAtUtc, d.Record?.CheckOutAtUtc,
                    d.Record?.Id, d.Record?.CheckInPhotoKey != null,
                    d.Record?.FaceMatchScore, d.Record?.FaceMatchStatus.ToString() ?? "NotChecked",
                    d.Record?.LateArrivalReason, d.Record?.EarlyDepartureReason,
                    d.Record?.WasOffline ?? false,
                    d.Record?.CheckInLatitude, d.Record?.CheckInLongitude,
                    d.Leave?.ToString(), d.LeaveAssignedBy, d.LeaveId,
                    d.Employee.Position, d.ManualBy,
                    d.FieldIn, d.FieldOut, d.FieldLat, d.FieldLng,
                    d.Record?.ClosedByFieldVisitId != null,
                    d.Employee.CanShareDevice);
            })
            .OrderBy(r => r.EmployeeName)
            .ToList();
    }

    /// <summary>
    /// Keeps a NIGHT worker on today's board when their shift began yesterday and crosses midnight.
    ///
    /// A record is keyed by the UTC day, but the board reads the local (Baku) day, and Baku is UTC+4 —
    /// so between 00:00 and 04:00 local a night worker who checked in at 21:00 is dated "yesterday" and
    /// would otherwise vanish from today's board while they are still standing at work. For anyone with
    /// nothing today, yesterday's row is substituted instead.
    ///
    /// Two guards, both essential, or this does more harm than the bug it fixes:
    ///   • Only OVERNIGHT shifts. A day worker who simply forgot to check out yesterday is not at work
    ///     today — carrying their open record over showed them "İşdə" with yesterday's check-in time,
    ///     a time that then reads as being in the future. Their unclosed day belongs on yesterday's
    ///     board and in /admin/open-records, not on today's.
    ///   • Only while the shift could still be running — up to its end time plus a couple of hours of
    ///     grace. Past that, even a night worker's open record is a forgotten check-out, not a shift in
    ///     progress, and must not sit on the board all day.
    ///
    /// Live board only; a historical day must never borrow from its neighbour.
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

        var nowLocal = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));

        var yesterday = await ComputeDayLiveAsync(day.AddDays(-1), withoutToday, ct);
        var stillOpen = yesterday
            .Where(d => d.Record?.CheckInAtUtc is not null && d.Record.CheckOutAtUtc is null)
            // Yesterday's date on both counts: the shift that is still running started then, and on a
            // schedule with per-day hours it is yesterday's pair that says whether it crosses midnight
            // and when it ends.
            .Where(d => d.Shift.IsOvernightOn(day.AddDays(-1))
                        && WithinOvernightWindow(d.Shift, nowLocal, day.AddDays(-1)))
            .ToDictionary(d => d.Employee.Id);
        if (stillOpen.Count == 0)
            return computed;

        return computed
            .Select(d => stillOpen.TryGetValue(d.Employee.Id, out var open) ? open : d)
            .ToList();
    }

    /// <summary>
    /// The status the LIVE board should show — the same as the computed one, except that a scheduled
    /// worker whose shift has not started yet reads "Pending" rather than "Absent".
    ///
    /// "Qayıb" (absent) on the board could not tell two very different things apart: someone who was
    /// due and did not come, and someone whose shift begins later today. A night worker on 21:00–07:00
    /// showed up red "Qayıb" all morning, indistinguishable from a real no-show, when their shift was
    /// still ten hours away. Until their start time plus its late grace has passed, that is not an
    /// absence — it is "not due yet". Once the grace passes with no check-in, it becomes a real Qayıb.
    ///
    /// Board only, and only for today: a finished past day's Absent is a genuine absence and stays.
    /// The <see cref="DailySummaryStatus"/> enum, the nightly summary, the tabel and payroll are all
    /// untouched — this is purely how the live board labels a not-yet-due worker.
    /// </summary>
    internal static string BoardDisplayStatus(
        DailySummaryStatus computed, EffectiveShift shift, bool isToday, TimeOnly nowLocal, DateOnly date)
    {
        if (!isToday || computed != DailySummaryStatus.Absent)
            return computed.ToString();

        // Integer minutes rather than TimeOnly.AddMinutes so a grace that would spill past midnight
        // (a shift starting at 23:5x) is capped at end-of-day instead of wrapping to 00:1x and
        // flipping the comparison. "Due" = shift start + the shift's own late threshold.
        // This day's start, not the shift's ordinary one — on a day the crew starts an hour later,
        // "not due yet" has to move with them or the board calls them absent for that hour.
        var startMin = (int)shift.HoursOn(date).Start.ToTimeSpan().TotalMinutes;
        var dueMin = Math.Min(startMin + shift.LateThresholdMinutes, 24 * 60 - 1);
        var nowMin = (int)nowLocal.ToTimeSpan().TotalMinutes;

        return nowMin <= dueMin ? "Pending" : nameof(DailySummaryStatus.Absent);
    }

    /// <summary>
    /// Whether an overnight shift that started yesterday could still be running at <paramref name="nowLocal"/>.
    /// True from midnight up to the shift's end time plus two hours of grace — so a 21:00–07:00 shift
    /// carries over until 09:00, covering both the "still working" window and a late check-out, then
    /// stops so a forgotten check-out does not linger on the board into the evening.
    /// </summary>
    internal static bool WithinOvernightWindow(EffectiveShift shift, TimeOnly nowLocal, DateOnly startedOn)
    {
        var cutoff = shift.HoursOn(startedOn).End.AddHours(2);
        // End is a morning time for an overnight shift, so the window is simply [00:00, end+2h].
        return nowLocal <= cutoff;
    }

    public async Task<(ReportAccess Access, ProblemsReport? Report)> GetProblemsAsync(
        DateOnly from, DateOnly to, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // An employee has no business seeing everyone else's failed scans.
        if (role == EmployeeRole.Employee)
            return (ReportAccess.Forbidden, null);

        if (to < from) (from, to) = (to, from);

        // Audit rows are stamped in UTC; translate the requested LOCAL range into a UTC window
        // [from 00:00, to+1 00:00).
        var localStart = from.ToDateTime(TimeOnly.MinValue);
        var localEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), _timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), _timeZone);

        // A week of audit rows is still small — pull them once and shape in memory.
        var dayLogs = await _db.AuditLogs
            .Where(a => a.CreatedAtUtc >= startUtc && a.CreatedAtUtc < endUtc)
            .OrderByDescending(a => a.CreatedAtUtc) // newest first — a range reads best most-recent-down
            .ToListAsync(ct);

        var empIds = dayLogs.Where(a => a.EmployeeId.HasValue).Select(a => a.EmployeeId!.Value).Distinct().ToList();
        var empById = await _db.Employees
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FullName, e.LocationId, e.Role })
            .ToDictionaryAsync(e => e.Id, e => (e.FullName, e.LocationId, e.Role), ct);
        var allLocations = await _db.Locations.ToListAsync(ct);
        var locationById = allLocations.ToDictionary(l => l.Id);
        var locationNames = allLocations.ToDictionary(l => l.Id, l => l.Name);

        // Manager scope: only Role==Employee workers in the locations they manage (plus their own
        // rejected scans) — same boundary as the boards. Admin: everything.
        List<Guid>? managed = role == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, requesterId, ct)
            : null;

        bool InScope(Guid? employeeId) =>
            managed is null
            || (employeeId is Guid id && empById.TryGetValue(id, out var e)
                && (id == requesterId || (managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee)));

        string NameOf(Guid? employeeId) =>
            employeeId is Guid id && empById.TryGetValue(id, out var e) ? e.FullName : "(naməlum)";

        string LocationOf(Guid? employeeId) =>
            employeeId is Guid id && empById.TryGetValue(id, out var e)
                ? locationNames.GetValueOrDefault(e.LocationId, "—") : "—";

        static string ActionOf(AuditEventType type) => type switch
        {
            // A check-IN rejection is really "the scan was rejected", direction unknown: geofence,
            // device and token checks all run before we decide in vs out, so an evening OutsideRadius
            // logged as CheckInRejected was a check-OUT attempt mislabelled "Giriş".
            AuditEventType.CheckInRejected => "Scan",
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
                return new ProblemRow(
                    a.CreatedAtUtc, a.EmployeeId, NameOf(a.EmployeeId), LocationOf(a.EmployeeId),
                    ActionOf(a.EventType), code, detail);
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

        // Geofence circles for the sites that actually had an OutsideRadius rejection — the map only
        // needs those. The employee's assigned location is the site they scanned (they scan their own
        // poster), so its centre + radius is the boundary their rejected point fell outside.
        var geofences = problems
            .Where(p => p.Reason == "OutsideRadius" && p.EmployeeId is Guid)
            .Select(p => empById.TryGetValue(p.EmployeeId!.Value, out var e) ? e.LocationId : (Guid?)null)
            .Where(id => id.HasValue)
            .Distinct()
            .Select(id => locationById.GetValueOrDefault(id!.Value))
            .Where(l => l is not null)
            .Select(l => new MapGeofence(l!.Name, l.Latitude, l.Longitude, l.RadiusMeters))
            .ToList();

        return (ReportAccess.Allowed, new ProblemsReport(from, to, problems.Count, successCount, summary, problems, geofences));
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
            .Select(g =>
            {
                // Attended = showed up at all (in and/or out); expected excludes days off, leave and
                // permission so a holiday-heavy day doesn't read as poor attendance. Same rule as the
                // live "today" rate, one day at a time.
                var attended = g.Count(x => x.Status is DailySummaryStatus.OnTime or DailySummaryStatus.Late or DailySummaryStatus.Incomplete);
                var expected = g.Count(x => x.Status is not (DailySummaryStatus.DayOff or DailySummaryStatus.OnLeave or DailySummaryStatus.Permission));
                var rate = expected > 0 ? Math.Round(attended * 100.0 / expected, 1) : 0;
                return new DailyTrendPoint(g.Key, g.Count(x => x.CheckInAtUtc != null), g.Count(x => x.CheckOutAtUtc != null), rate);
            })
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

        // Still-onboarding headcount, TODAY only. It cannot come from `summaries` — LoadDayRowsAsync
        // deliberately filters these people out of today's rows — so the roster is asked directly.
        var onboardingCount = 0;
        if (from <= todayLocal && todayLocal <= to)
        {
            var (rosterAccess, roster) = await ScopedEmployeesAsync(locationId, requesterId, role, ct);
            if (rosterAccess == ReportAccess.Allowed)
            {
                var isOnboarding = await OnboardingCheckerAsync(roster.Select(e => e.Id).ToList(), ct);
                onboardingCount = roster.Count(e => isOnboarding(e.Id, todayLocal));
            }
        }

        var report = new DashboardReport(
            from, to, label,
            totalCheckIns, totalCheckOuts, lateCount, absentCount, incompleteCount, stillAtWorkCount,
            dayOffCount, leaveCount, permissionCount,
            totalWorkedHours, overtimeHours, outsideRadiusCount, activeDeviceCount,
            checkInOutRatio, lateRate, outsideRadiusRate, avgDailyOperations,
            trend, weekday, topLate, onboardingCount);

        return (ReportAccess.Allowed, report);
    }

    public async Task<(ReportAccess Access, ShiftMismatchReport? Report)> GetShiftMismatchAsync(
        int days, Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // Whose shift is wrong is a scheduling question about other people; an employee has no
        // business with it, the same boundary the other reports draw.
        if (role == EmployeeRole.Employee)
            return (ReportAccess.Forbidden, null);

        days = Math.Clamp(days, 7, 90);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        var from = today.AddDays(-days);

        // Manager scope: their branches, plain employees only — identical to the boards. Read BEFORE
        // the employee query so the database does the narrowing rather than the loop below.
        List<Guid>? managed = role == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, requesterId, ct)
            : null;

        var employees = await _db.Employees
            .Where(e => e.IsActive)
            .Where(e => managed == null || (managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee))
            .Select(e => new
            {
                e.Id, e.FullName, e.Position, e.LocationId, e.ScheduleId,
                e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor,
            })
            .ToListAsync(ct);

        var locations = await _db.Locations.ToDictionaryAsync(l => l.Id, ct);
        var schedules = await _db.Schedules.ToDictionaryAsync(s => s.Id, ct);

        var empIds = employees.Select(e => e.Id).ToHashSet();
        var scans = await _db.AttendanceRecords
            .Where(a => a.AttendanceDate >= from && a.CheckInAtUtc != null)
            .Select(a => new { a.EmployeeId, a.AttendanceDate, a.CheckInAtUtc })
            .ToListAsync(ct);

        var byEmployee = scans
            .Where(a => empIds.Contains(a.EmployeeId))
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<ShiftMismatchRow>();
        var judged = 0;

        foreach (var e in employees)
        {
            if (!byEmployee.TryGetValue(e.Id, out var mine) || mine.Count < ShiftFit.MinScans) continue;
            if (!locations.TryGetValue(e.LocationId, out var location)) continue;

            var schedule = e.ScheduleId is Guid sid ? schedules.GetValueOrDefault(sid) : null;
            var shift = EffectiveShift.Resolve(
                e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor,
                schedule, location);

            judged++;
            var off = 0;
            var worst = 0;
            TimeOnly? earliest = null, latest = null;

            foreach (var a in mine)
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(a.CheckInAtUtc!.Value, _timeZone);
                var at = TimeOnly.FromDateTime(local);
                // The hours that applied ON THAT DAY — a crew whose weekend starts later must not be
                // flagged every Saturday for keeping to the shift it was actually given.
                var expected = shift.HoursOn(DateOnly.FromDateTime(local)).Start;

                if (ShiftFit.IsOff(at, expected)) off++;
                worst = Math.Max(worst, ShiftFit.GapHours(at, expected));
                if (earliest is null || at < earliest) earliest = at;
                if (latest is null || at > latest) latest = at;
            }

            if (!ShiftFit.ShouldFlag(mine.Count, off)) continue;

            var (start, end) = (shift.Start, shift.End);
            rows.Add(new ShiftMismatchRow(
                e.Id, e.FullName, e.Position ?? string.Empty,
                location.Name,
                $"{start.Hour:D2}:{start.Minute:D2}–{end.Hour:D2}:{end.Minute:D2}",
                shift.ScheduleName,
                mine.Count, off, worst,
                earliest!.Value, latest!.Value));
        }

        // Worst first: the biggest gap is the one most likely to be a genuinely misfiled person, and
        // the one costing whole days rather than minutes.
        rows.Sort((a, b) => b.WorstGapHours.CompareTo(a.WorstGapHours));

        return (ReportAccess.Allowed, new ShiftMismatchReport(days, judged, rows));
    }


    public async Task<(ReportAccess Access, IReadOnlyList<StuckDeviceRow> Rows)> GetStuckDevicesAsync(
        Guid requesterId, EmployeeRole role, CancellationToken ct = default)
    {
        // The register behind "kimdir, necə tapaq": measured on 02.09, twenty-odd people were still
        // GPS-blocked and the only way to name them was a hand-written SQL. A stuck phone loses a
        // recorded day EVERY day until a human touches it, so the list has to be a screen an admin
        // opens, not a query an engineer runs.
        if (role == EmployeeRole.Employee)
            return (ReportAccess.Forbidden, []);

        var (access, employees) = await ScopedEmployeesAsync(null, requesterId, role, ct);
        if (access == ReportAccess.Forbidden)
            return (ReportAccess.Forbidden, []);

        var ids = employees.Select(e => e.Id).ToList();
        var since = DateTime.UtcNow.AddDays(-30);

        // Device-family failures only: things a phone setting fixes. OutsideRadius or a shared-device
        // refusal are different problems with different screens.
        var failures = await _db.AuditLogs
            .Where(a => a.EmployeeId != null && ids.Contains(a.EmployeeId.Value)
                        && a.CreatedAtUtc >= since && a.Reason != null
                        && (a.Reason.StartsWith("Gps") || a.Reason.StartsWith("Camera")
                            || a.Reason.StartsWith("ScannerLoadFailed")))
            .Select(a => new { EmployeeId = a.EmployeeId!.Value, a.CreatedAtUtc, a.Reason })
            .ToListAsync(ct);
        if (failures.Count == 0)
            return (ReportAccess.Allowed, []);

        var byEmployee = failures.GroupBy(f => f.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var affected = byEmployee.Keys.ToList();

        var lastScans = await _db.AttendanceRecords
            .Where(r => affected.Contains(r.EmployeeId) && r.CheckInAtUtc != null)
            .GroupBy(r => r.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Last = g.Max(r => r.CheckInAtUtc) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Last, ct);

        var phones = await _db.Employees
            .Where(e => affected.Contains(e.Id))
            .Select(e => new { e.Id, e.PhoneNumber })
            .ToDictionaryAsync(x => x.Id, x => x.PhoneNumber, ct);

        var locationNames = await _db.Locations.ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var nameById = employees.ToDictionary(e => e.Id, e => (e.FullName, e.LocationId));

        var rows = new List<StuckDeviceRow>();
        foreach (var (employeeId, evts) in byEmployee)
        {
            var lastFailure = evts.Max(e => e.CreatedAtUtc);
            var lastScan = lastScans.TryGetValue(employeeId, out var l) ? l : null;

            // Not stuck if they have scanned successfully SINCE the last failure — a one-off hiccup
            // that resolved itself is exactly the row this list must not drown in.
            if (lastScan is DateTime ok && ok > lastFailure)
                continue;

            var reasons = evts.Select(e => e.Reason!.Split('|')[0]).Distinct().OrderBy(r => r).ToList();
            // Newest report that carried a platform tag, if any ("...|84·ios·denied" or "...|ios").
            var platform = evts.OrderByDescending(e => e.CreatedAtUtc)
                .Select(e => e.Reason!.Split('|').ElementAtOrDefault(1))
                .Where(d => d != null)
                .SelectMany(d => d!.Split('·'))
                .FirstOrDefault(t => t is "ios" or "android");

            var (fullName, locationId) = nameById.TryGetValue(employeeId, out var n) ? n : ("?", Guid.Empty);
            rows.Add(new StuckDeviceRow(
                employeeId, fullName,
                locationNames.TryGetValue(locationId, out var ln) ? ln : "-",
                phones.TryGetValue(employeeId, out var ph) ? ph : null,
                lastFailure, evts.Count,
                string.Join(", ", reasons),
                platform,
                lastScan));
        }

        return (ReportAccess.Allowed, rows.OrderByDescending(r => r.FailureCount30d).ToList());
    }

}
