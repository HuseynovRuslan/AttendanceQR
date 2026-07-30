using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

public sealed class AttendanceQueryService : IAttendanceQueryService
{
    private readonly AppDbContext _db;

    public AttendanceQueryService(AppDbContext db) => _db = db;

    public Task<IReadOnlyList<AttendanceRecordDto>> GetOwnRecordsAsync(Guid employeeId, CancellationToken ct = default)
        => QueryRecordsAsync(employeeId, ct);

    public async Task<(AttendanceAccess Access, IReadOnlyList<AttendanceRecordDto> Records)> GetForEmployeeAsync(
        Guid targetEmployeeId, Guid requesterId, EmployeeRole requesterRole, CancellationToken ct = default)
    {
        // One central rule (LocationScopeRules) — the same manager scope the reports/export use.
        var allowed = await LocationScopeRules.CanAccessEmployeeAsync(_db, requesterId, requesterRole, targetEmployeeId, ct);

        if (!allowed)
            return (AttendanceAccess.Forbidden, Array.Empty<AttendanceRecordDto>());

        return (AttendanceAccess.Allowed, await QueryRecordsAsync(targetEmployeeId, ct));
    }

    private async Task<IReadOnlyList<AttendanceRecordDto>> QueryRecordsAsync(Guid employeeId, CancellationToken ct)
    {
        var rows = await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.AttendanceDate)
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
