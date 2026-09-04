using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

public sealed class AttendanceQueryService : IAttendanceQueryService
{
    private readonly AppDbContext _db;

    public AttendanceQueryService(AppDbContext db) => _db = db;

    // The employee's own history (Home + Scan) and an admin's per-employee view are BOUNDED. The hot
    // path used to pull the WHOLE history unbounded, which grew linearly with every scan ever made and
    // was fetched on every Home load AND every Scan-page open. Recent-first; the full history lives in
    // the monthly Tabel / reports, not this list.
    private const int OwnRecordsCap = 90;     // ~a quarter, plenty for the mobile history view
    private const int AdminRecordsCap = 200;  // admin per-employee list; full history via Tabel/reports

    public Task<IReadOnlyList<AttendanceRecordDto>> GetOwnRecordsAsync(Guid employeeId, CancellationToken ct = default)
        => QueryRecordsAsync(employeeId, OwnRecordsCap, ct);

    public async Task<AttendanceRecordDto?> GetTodayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        // Single row via the (EmployeeId, AttendanceDate) unique index — an exact-match lookup, not a
        // history scan. This is what the Scan page waits on before opening the camera.
        var r = await _db.AttendanceRecords
            .Where(x => x.EmployeeId == employeeId && x.AttendanceDate == date)
            .Select(x => new
            {
                x.Id, x.AttendanceDate, x.LocationId, x.CheckInAtUtc, x.CheckOutAtUtc,
                Status = x.Status.ToString(), x.FaceMatchScore, FaceMatchStatus = x.FaceMatchStatus.ToString(),
                x.ManualByEmployeeId, x.ClosedByFieldVisitId
            })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        string? manualBy = null;
        if (r.ManualByEmployeeId is Guid mid)
            manualBy = await _db.Employees.Where(e => e.Id == mid).Select(e => e.FullName).FirstOrDefaultAsync(ct);

        return new AttendanceRecordDto(r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc,
            r.Status, r.FaceMatchScore, r.FaceMatchStatus, manualBy, r.ClosedByFieldVisitId != null);
    }

    public async Task<(AttendanceAccess Access, IReadOnlyList<AttendanceRecordDto> Records)> GetForEmployeeAsync(
        Guid targetEmployeeId, Guid requesterId, EmployeeRole requesterRole, CancellationToken ct = default)
    {
        // One central rule (LocationScopeRules) — the same manager scope the reports/export use.
        var allowed = await LocationScopeRules.CanAccessEmployeeAsync(_db, requesterId, requesterRole, targetEmployeeId, ct);

        if (!allowed)
            return (AttendanceAccess.Forbidden, Array.Empty<AttendanceRecordDto>());

        return (AttendanceAccess.Allowed, await QueryRecordsAsync(targetEmployeeId, AdminRecordsCap, ct));
    }

    private async Task<IReadOnlyList<AttendanceRecordDto>> QueryRecordsAsync(Guid employeeId, int take, CancellationToken ct)
    {
        var rows = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.AttendanceDate)
            .Take(take)
            .Select(r => new
            {
                r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc,
                Status = r.Status.ToString(), r.FaceMatchScore, FaceMatchStatus = r.FaceMatchStatus.ToString(),
                r.ManualByEmployeeId, r.ClosedByFieldVisitId
            })
            .ToListAsync(ct);

        // Resolve the "edited by hand by" ids to names in one query (same tenant, so the query filter
        // finds them). A handful of distinct admins at most.
        var manualIds = rows.Where(r => r.ManualByEmployeeId != null)
            .Select(r => r.ManualByEmployeeId!.Value).Distinct().ToList();
        var manualNames = manualIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Employees.Where(e => manualIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var posterDays = rows.Select(r => r.AttendanceDate).ToHashSet();

        // Field days with NO poster scan of their own.
        //
        // Without this the list is a lie by omission: a «səyyar» day writes a FieldVisit and no
        // AttendanceRecord, so a person who worked nine hours at a site with no poster opened their
        // own history and found the date simply missing — while the summary counted 543 minutes and
        // the payroll paid for them. They concluded they had not been recorded, which is the only
        // reasonable conclusion from what the screen showed them.
        //
        // Only days the poster list does not already cover: when both exist, the poster row already
        // carries the merged times and adding a second row for the same date would read as two days.
        var firstDate = rows.Count == 0 ? (DateOnly?)null : rows[^1].AttendanceDate;
        var fieldDays = await _db.FieldVisits
            .Where(v => v.EmployeeId == employeeId
                        && v.CheckInAtUtc != null
                        && v.Status != FieldVisitStatus.Cancelled
                        && (firstDate == null || v.VisitDate >= firstDate))
            .GroupBy(v => v.VisitDate)
            .Select(g => new
            {
                Date = g.Key,
                // Earliest arrival and, only once every visit that day is closed, the last departure —
                // the same rule the board and the reports use, so the three cannot disagree.
                In = g.Min(x => x.CheckInAtUtc),
                Out = g.All(x => x.CheckOutAtUtc != null) ? g.Max(x => x.CheckOutAtUtc) : null,
            })
            .OrderByDescending(x => x.Date)
            .Take(take)
            .ToListAsync(ct);

        var fieldRows = fieldDays
            .Where(f => !posterDays.Contains(f.Date))
            .Select(f => new AttendanceRecordDto(
                // No AttendanceRecord exists, so there is no record id to give. Empty rather than
                // invented: every consumer of this id looks a record up by it, and a fabricated one
                // would 404 somewhere far from here.
                Guid.Empty, f.Date, Guid.Empty, f.In, f.Out,
                f.Out is null ? "CheckedIn" : "Completed",
                null, "NotChecked", null, false, IsFieldDay: true));

        return rows.Select(r => new AttendanceRecordDto(
                r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc, r.Status,
                r.FaceMatchScore, r.FaceMatchStatus,
                r.ManualByEmployeeId is Guid mid ? manualNames.GetValueOrDefault(mid) : null,
                r.ClosedByFieldVisitId != null))
            .Concat(fieldRows)
            .OrderByDescending(r => r.AttendanceDate)
            .Take(take)
            .ToList();
    }
}
