using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Infrastructure.Services;

public sealed record AttendanceRecordDto(
    Guid RecordId,
    DateOnly AttendanceDate,
    Guid LocationId,
    DateTime? CheckInAtUtc,
    DateTime? CheckOutAtUtc,
    string Status,
    int? FaceMatchScore,
    string FaceMatchStatus);

/// <summary>Outcome of a resource-level access check for another employee's records.</summary>
public enum AttendanceAccess
{
    Allowed,
    Forbidden
}

/// <summary>
/// Read side of attendance. Enforces <b>resource-level</b> authorization: the caller's role alone
/// does not decide access — who they are relative to the requested employee does.
/// </summary>
public interface IAttendanceQueryService
{
    /// <summary>The caller's own records — no cross-employee access is possible.</summary>
    Task<IReadOnlyList<AttendanceRecordDto>> GetOwnRecordsAsync(Guid employeeId, CancellationToken ct = default);

    /// <summary>
    /// Records for <paramref name="targetEmployeeId"/>, gated by who the requester is:
    /// Admin → anyone; Manager → only Employees in their own location; Employee → only themselves.
    /// </summary>
    Task<(AttendanceAccess Access, IReadOnlyList<AttendanceRecordDto> Records)> GetForEmployeeAsync(
        Guid targetEmployeeId, Guid requesterId, EmployeeRole requesterRole, CancellationToken ct = default);
}
