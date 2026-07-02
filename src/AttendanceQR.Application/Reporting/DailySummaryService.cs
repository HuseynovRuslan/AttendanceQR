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
        var employees = await _db.Employees
            .Where(e => e.IsActive && e.ActivatedAtUtc != null)
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
        var summary = new DailySummary
        {
            EmployeeId = employeeId,
            LocationId = locationId,
            SummaryDate = date
        };

        // No record → the employee never showed up.
        if (record is null || record.CheckInAtUtc is null)
        {
            summary.Status = DailySummaryStatus.Absent;
            return summary;
        }

        summary.CheckInAtUtc = record.CheckInAtUtc;
        summary.CheckOutAtUtc = record.CheckOutAtUtc;

        // Checked in but never out.
        if (record.CheckOutAtUtc is null)
        {
            summary.Status = DailySummaryStatus.Incomplete;
            return summary;
        }

        // Timezone: CheckInAtUtc is a UTC instant; ShiftStart/ShiftEnd are LOCAL wall-clock times.
        // Convert the instant to local (Asia/Baku = UTC+4) before comparing, otherwise a 05:45Z
        // check-in would look like it beat a 09:00 shift when it is really 09:45 local (45 min late).
        var localCheckIn = TimeZoneInfo.ConvertTimeFromUtc(record.CheckInAtUtc.Value, _timeZone);
        var localCheckOut = TimeZoneInfo.ConvertTimeFromUtc(record.CheckOutAtUtc.Value, _timeZone);

        var minutesAfterStart =
            (TimeOnly.FromDateTime(localCheckIn).ToTimeSpan() - location.ShiftStart.ToTimeSpan()).TotalMinutes;

        summary.WorkedMinutes = (int)Math.Round((record.CheckOutAtUtc.Value - record.CheckInAtUtc.Value).TotalMinutes);

        if (minutesAfterStart > location.LateThresholdMinutes)
        {
            summary.Status = DailySummaryStatus.Late;
            summary.LateMinutes = (int)Math.Round(minutesAfterStart);
        }
        else
        {
            summary.Status = DailySummaryStatus.OnTime;
        }

        var overtimeMinutes =
            (TimeOnly.FromDateTime(localCheckOut).ToTimeSpan() - location.ShiftEnd.ToTimeSpan()).TotalMinutes;
        if (overtimeMinutes > 0)
            summary.OvertimeMinutes = (int)Math.Round(overtimeMinutes);

        return summary;
    }
}
