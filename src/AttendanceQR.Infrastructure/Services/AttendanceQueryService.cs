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
                x.ManualByEmployeeId
            })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        string? manualBy = null;
        if (r.ManualByEmployeeId is Guid mid)
            manualBy = await _db.Employees.Where(e => e.Id == mid).Select(e => e.FullName).FirstOrDefaultAsync(ct);

        return new AttendanceRecordDto(r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc,
            r.Status, r.FaceMatchScore, r.FaceMatchStatus, manualBy);
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
                r.ManualByEmployeeId
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

        return rows.Select(r => new AttendanceRecordDto(
            r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc, r.Status,
            r.FaceMatchScore, r.FaceMatchStatus,
            r.ManualByEmployeeId is Guid mid ? manualNames.GetValueOrDefault(mid) : null)).ToList();
    }
}
