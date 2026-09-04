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
    string FaceMatchStatus,
    // Name of the admin/manager who created or changed this record by hand (null for a real scan), so
    // both the employee's own history and an admin can see a manually-entered day is attributable.
    string? ManualByName = null,
    // True when the check-out came from this employee's own field check-out (they went home from the
    // site) rather than a poster scan. Not the same thing as ManualByName — see
    // AttendanceRecord.ClosedByFieldVisitId for why the two are kept apart.
    bool ClosedByFieldVisit = false,
    /// <summary>
    /// This day has no poster scan at all — the times come from a «səyyar» field visit.
    ///
    /// It exists because the day was INVISIBLE to the person who worked it. This list reads
    /// AttendanceRecords, a field day writes only a FieldVisit, and so a driver who checked in at a
    /// site with no poster, worked nine hours and checked out saw his own history skip the date
    /// entirely — while the payroll counted every minute of it. He assumed he had not been recorded
    /// and came to ask. The label is what tells him which kind of day it was.
    /// </summary>
    bool IsFieldDay = false);

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

    /// <summary>The caller's record for a single day (today), or null. The Scan page only needs today's
    /// status to start, so it queries this one row instead of pulling the whole history.</summary>
    Task<AttendanceRecordDto?> GetTodayAsync(Guid employeeId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// Records for <paramref name="targetEmployeeId"/>, gated by who the requester is:
    /// Admin → anyone; Manager → only Employees in their own location; Employee → only themselves.
    /// </summary>
    Task<(AttendanceAccess Access, IReadOnlyList<AttendanceRecordDto> Records)> GetForEmployeeAsync(
        Guid targetEmployeeId, Guid requesterId, EmployeeRole requesterRole, CancellationToken ct = default);
}
