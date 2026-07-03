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

    public DailySummaryService(AppDbContext db, AppOptions options)
    {
        _db = db;
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
    }

    public async Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        // Admins are system operators, not on-site staff — exclude them so they don't accumulate a
        // permanent, meaningless "Absent" history (mirrors the live "today" board's exclusion).
        var employees = await _db.Employees
            .Where(e => e.IsActive && e.ActivatedAtUtc != null && e.Role != EmployeeRole.Admin)
            .Select(e => new { e.Id, e.LocationId })
            .ToListAsync(ct);

        var locationIds = employees.Select(e => e.LocationId).Distinct().ToList();
        var locations = await _db.Locations
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        var records = await _db.AttendanceRecords
            .Where(r => r.AttendanceDate == date)
            .ToDictionaryAsync(r => r.EmployeeId, ct);

        // Existing summaries for the date → upsert (idempotent; the unique index also guards this).
        var existing = await _db.DailySummaries
            .Where(s => s.SummaryDate == date)
            .ToDictionaryAsync(s => s.EmployeeId, ct);

        foreach (var emp in employees)
        {
            if (!locations.TryGetValue(emp.LocationId, out var location))
                continue; // defensive: employee's location vanished

            records.TryGetValue(emp.Id, out var record);
            var computed = Compute(emp.Id, emp.LocationId, date, record, location);

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

    private DailySummary Compute(Guid employeeId, Guid locationId, DateOnly date, AttendanceRecord? record, Location location)
    {
        // Shared timezone/late/overtime logic (also used by the live "today" query).
        var c = AttendanceCalculator.Compute(record, location, _timeZone);
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
