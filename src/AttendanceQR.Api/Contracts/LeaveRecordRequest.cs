using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

/// <summary>Create an approved leave/permission range for an employee. Both dates inclusive.</summary>
public record LeaveRecordRequest(
    Guid EmployeeId,
    DateOnly FromDate,
    DateOnly ToDate,
    LeaveType Type,
    string? Note,
    /// <summary>
    /// Several people on the same dates and the same type — the shape the work actually has. Two
    /// thirds of every leave ever filed is a one-or-two-day "İstirahət", and a rest day is something
    /// a crew takes together: filing it was one record per person, the same four fields retyped down
    /// a list. When present this wins; <see cref="EmployeeId"/> stays for a single row.
    /// </summary>
    IReadOnlyList<Guid>? EmployeeIds = null)
{
    /// <summary>Everyone this request is for, however it was addressed.</summary>
    public IReadOnlyList<Guid> Subjects =>
        EmployeeIds is { Count: > 0 } many ? many.Distinct().ToList() : [EmployeeId];
}
