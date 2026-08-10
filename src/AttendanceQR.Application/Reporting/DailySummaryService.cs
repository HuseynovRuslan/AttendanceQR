using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Application.Reporting;

public interface IDailySummaryService
{
    /// <summary>
    /// (Re)computes the day's summary for every active, activated employee. Idempotent — existing
    /// rows for the date are updated in place, so re-running never creates duplicates. Returns the
    /// number of employees summarized.
    /// </summary>
    Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default);
}

public sealed class DailySummaryService : IDailySummaryService
{
    private readonly AppDbContext _db;
    private readonly TimeZoneInfo _timeZone;
    private readonly string[] _hiddenEmails;

    public DailySummaryService(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        _hiddenEmails = options.HiddenEmailList();
    }

    public async Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        // A DailySummary is a FINISHED-day record. Never generate today or a future date: today is always
        // computed live (the board and reports never read today from here), and a future day has no
        // records yet — so it would mark every employee Absent. This used to happen wholesale: a leave
        // ending weeks out made RecomputeRange call this for every future day, pre-creating Absent rows
        // for all staff up to the leave's end. Guard it here, at the one place, so no caller can.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _timeZone));
        if (date >= today)
            return 0;

        // Only the system/root accounts in HiddenEmails are excluded; admins/managers who also clock in
        // (e.g. a director who scans) get summarised like any staff (mirrors the live "today" board).
        var employees = await _db.Employees
            .Where(e => e.IsActive && e.ActivatedAtUtc != null && (e.Email == null || !_hiddenEmails.Contains(e.Email.ToLower())))
            .Select(e => new { e.Id, e.LocationId, e.ScheduleId, e.WorkStart, e.WorkEnd, e.WorkCycleDays, e.WorkCycleOnDays, e.WorkCycleAnchor, e.ActivatedAtUtc })
            .ToListAsync(ct);

        // A handful of rows per tenant, so they are loaded whole and looked up in memory rather
        // than joined into every employee projection.
        var schedules = await _db.Schedules.ToDictionaryAsync(sc => sc.Id, ct);

        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();
        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == date)
            .ToDictionaryAsync(r => r.EmployeeId, ct);

        // Field visits are attendance too. A worker sent to a site with no QR poster proves presence
        // with GPS + time instead of a scan, so a day spent in the field must count as WORKED here —
        // this table is what the tabel and the payroll deduction read. Without it a driver who spent
        // the whole month on field visits reads as Qayıb every day and loses a month's pay.
        // Aggregated per employee: earliest arrival, latest departure, and whether any visit is still
        // open (that day is Incomplete, exactly like a forgotten office check-out).
        var fieldByEmployee = await _db.FieldVisits
            .Where(v => v.VisitDate == date && v.Status != FieldVisitStatus.Cancelled && v.CheckInAtUtc != null)
            .GroupBy(v => v.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                FirstIn = g.Min(v => v.CheckInAtUtc),
                LastOut = g.Max(v => v.CheckOutAtUtc),
                AnyOpen = g.Any(v => v.CheckOutAtUtc == null),
            })
            .ToDictionaryAsync(x => x.EmployeeId, ct);

        // Admin-declared non-working days for this date: either global (LocationId == null) or
        // specific to one of the locations in play. A location is "off" if a matching row exists.
        var nonWorkingLocationIds = await _db.NonWorkingDays
            .Where(n => n.Date == date && (n.LocationId == null || locationIds.Contains(n.LocationId.Value)))
            .Select(n => n.LocationId)
            .ToListAsync(ct);
        var isGloballyNonWorking = nonWorkingLocationIds.Contains(null);
        var nonWorkingLocationIdSet = nonWorkingLocationIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        // Approved leave/permission covering this date, per employee — takes priority over both
        // Absent and DayOff (defensive GroupBy/First in case an admin ever creates overlapping
        // ranges for the same employee).
        var employeeIds = employees.Select(e => e.Id).ToList();
        var leaveByEmployee = await _db.LeaveRecords
            .Where(l => l.FromDate <= date && l.ToDate >= date && employeeIds.Contains(l.EmployeeId))
            .GroupBy(l => l.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Type = g.First().Type })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Type, ct);

        // Existing summaries for the date → upsert (idempotent; the unique index also guards this).
        var existing = await _db.DailySummaries
            .Where(s => s.SummaryDate == date)
            .ToDictionaryAsync(s => s.EmployeeId, ct);

        foreach (var emp in employees)
        {
            if (!locations.TryGetValue(emp.LocationId, out var location))
                continue; // defensive: employee's location vanished

            // Not yet onboarded on this date: nobody can scan before they activate, so a working day
            // before activation is not an absence — it's a day the person didn't exist here. Skip it,
            // so a mid-month hire isn't billed a string of phantom Qayıb days before they started.
            if (emp.ActivatedAtUtc is DateTime act
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(act, _timeZone)) > date)
                continue;

            var shift = EffectiveShift.Resolve(
                emp.WorkStart, emp.WorkEnd, emp.WorkCycleDays, emp.WorkCycleOnDays, emp.WorkCycleAnchor,
                emp.ScheduleId is Guid sid ? schedules.GetValueOrDefault(sid) : null, location);

            var isWorkingDay = shift.IsWorkingDay(date)
                                && !isGloballyNonWorking
                                && !nonWorkingLocationIdSet.Contains(location.Id);
            LeaveType? leaveType = leaveByEmployee.TryGetValue(emp.Id, out var lt) ? lt : null;
            var noRecordStatus = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leaveType);

            records.TryGetValue(emp.Id, out var record);

            // An office record always wins: someone who scanned at their branch is judged by that scan,
            // and folding field minutes into it would double-count overlapping time. Field visits only
            // fill a day that has NO scan — the same rule the live "today" board uses. Note this
            // overrides DayOff/OnLeave too: turning up to a site on a rest day or mid-vacation is worked
            // time, exactly as the LeaveRecord rule already says for a scan on a leave day.
            var fieldRecord = record?.CheckInAtUtc is null && fieldByEmployee.TryGetValue(emp.Id, out var fv)
                ? new AttendanceRecord
                {
                    EmployeeId = emp.Id,
                    LocationId = emp.LocationId,
                    AttendanceDate = date,
                    CheckInAtUtc = fv.FirstIn,
                    // Any still-open visit leaves the day open, whatever the other visits did.
                    CheckOutAtUtc = fv.AnyOpen ? null : fv.LastOut,
                }
                : null;

            var computed = Compute(emp.Id, emp.LocationId, date, record ?? fieldRecord, shift, isWorkingDay, noRecordStatus);

            // A field visit has no fixed arrival to be late for — the manager decides when they are
            // sent and the travel is not theirs to control. So a field-derived day never carries
            // lateness; "Late" would invent a fault out of a dispatch decision. (It changes nothing
            // the tabel shows — Late and OnTime are both "İ" — but it keeps the stored record honest.)
            if (fieldRecord is not null)
            {
                if (computed.Status == DailySummaryStatus.Late)
                    computed.Status = DailySummaryStatus.OnTime;
                computed.LateMinutes = 0;
            }

            if (existing.TryGetValue(emp.Id, out var summary))
            {
                summary.LocationId = computed.LocationId;
                summary.CheckInAtUtc = computed.CheckInAtUtc;
                summary.CheckOutAtUtc = computed.CheckOutAtUtc;
                summary.WorkedMinutes = computed.WorkedMinutes;
                summary.Status = computed.Status;
                summary.LateMinutes = computed.LateMinutes;
                summary.OvertimeMinutes = computed.OvertimeMinutes;
            }
            else
            {
                _db.DailySummaries.Add(computed);
            }
        }

        await _db.SaveChangesAsync(ct);
        return employees.Count;
    }

    private DailySummary Compute(
        Guid employeeId, Guid locationId, DateOnly date, AttendanceRecord? record, EffectiveShift shift,
        bool isWorkingDay, DailySummaryStatus noRecordStatus)
    {
        // Shared timezone/late/overtime logic (also used by the live "today" query), against the shift
        // already resolved for this employee — the same one the scan endpoint judged them by.
        var c = AttendanceCalculator.Compute(record, shift, _timeZone, isWorkingDay, noRecordStatus);
        return new DailySummary
        {
            EmployeeId = employeeId,
            LocationId = locationId,
            SummaryDate = date,
            CheckInAtUtc = record?.CheckInAtUtc,
            CheckOutAtUtc = record?.CheckOutAtUtc,
            Status = c.Status,
            WorkedMinutes = c.WorkedMinutes,
            LateMinutes = c.LateMinutes,
            OvertimeMinutes = c.OvertimeMinutes,
        };
    }
}
