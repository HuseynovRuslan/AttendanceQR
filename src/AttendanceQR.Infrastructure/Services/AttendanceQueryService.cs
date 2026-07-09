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
        => await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.AttendanceDate)
            .Select(r => new AttendanceRecordDto(
                r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc, r.Status.ToString(),
                r.FaceMatchScore, r.FaceMatchStatus.ToString()))
            .ToListAsync(ct);
}
