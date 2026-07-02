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
        var allowed = requesterRole switch
        {
            EmployeeRole.Admin => true,
            EmployeeRole.Manager => await CanManagerAccessAsync(requesterId, targetEmployeeId, ct),
            // Employee: strictly their own records — this is the resource-level check that a plain
            // [Authorize] cannot express.
            _ => requesterId == targetEmployeeId
        };

        if (!allowed)
            return (AttendanceAccess.Forbidden, Array.Empty<AttendanceRecordDto>());

        return (AttendanceAccess.Allowed, await QueryRecordsAsync(targetEmployeeId, ct));
    }

    // A manager sees their own records and their team's — Employees in the same location — but not
    // peer managers or admins.
    private async Task<bool> CanManagerAccessAsync(Guid managerId, Guid targetId, CancellationToken ct)
    {
        if (managerId == targetId)
            return true;

        var managerLocation = await _db.Employees
            .Where(e => e.Id == managerId)
            .Select(e => (Guid?)e.LocationId)
            .FirstOrDefaultAsync(ct);

        var target = await _db.Employees
            .Where(e => e.Id == targetId)
            .Select(e => new { e.LocationId, e.Role })
            .FirstOrDefaultAsync(ct);

        if (managerLocation is null || target is null)
            return false;

        return target.Role == EmployeeRole.Employee && target.LocationId == managerLocation.Value;
    }

    private async Task<IReadOnlyList<AttendanceRecordDto>> QueryRecordsAsync(Guid employeeId, CancellationToken ct)
        => await _db.AttendanceRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.AttendanceDate)
            .Select(r => new AttendanceRecordDto(
                r.Id, r.AttendanceDate, r.LocationId, r.CheckInAtUtc, r.CheckOutAtUtc, r.Status.ToString()))
            .ToListAsync(ct);
}
