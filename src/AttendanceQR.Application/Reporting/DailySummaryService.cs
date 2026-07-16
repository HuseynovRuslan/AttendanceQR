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
        // Only the system/root accounts in HiddenEmails are excluded; admins/managers who also clock in
        // (e.g. a director who scans) get summarised like any staff (mirrors the live "today" board).
        var employees = await _db.Employees
            .Where(e => e.IsActive && e.ActivatedAtUtc != null && !_hiddenEmails.Contains(e.Email.ToLower()))
            .Select(e => new { e.Id, e.LocationId })
            .ToListAsync(ct);

        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();
        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == date)
            .ToDictionaryAsync(r => r.EmployeeId, ct);

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

            var isWorkingDay = AttendanceCalculator.IsWorkingDayOfWeek(location.WorkDaysMask, date.DayOfWeek)
                                && !isGloballyNonWorking
                                && !nonWorkingLocationIdSet.Contains(location.Id);
            LeaveType? leaveType = leaveByEmployee.TryGetValue(emp.Id, out var lt) ? lt : null;
            var noRecordStatus = AttendanceCalculator.ResolveNoRecordStatus(isWorkingDay, leaveType);

            records.TryGetValue(emp.Id, out var record);
            var computed = Compute(emp.Id, emp.LocationId, date, record, location, isWorkingDay, noRecordStatus);

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
        Guid employeeId, Guid locationId, DateOnly date, AttendanceRecord? record, Location location,
        bool isWorkingDay, DailySummaryStatus noRecordStatus)
    {
        // Shared timezone/late/overtime logic (also used by the live "today" query).
        var c = AttendanceCalculator.Compute(record, location, _timeZone, isWorkingDay, noRecordStatus);
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
