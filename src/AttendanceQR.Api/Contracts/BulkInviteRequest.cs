using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Add many employees at once, sharing one location + role. Each row becomes an invited employee
/// with its own activation link. Rows are validated independently — a bad row (duplicate phone,
/// missing name) is reported back without blocking the rest.
/// </summary>
public record BulkInviteRequest(
    Guid LocationId,
    EmployeeRole Role,
    IReadOnlyList<BulkInviteRow> Rows);

public record BulkInviteRow(
    string FullName,
    string? PhoneNumber = null,
    string? Email = null,
    string? Position = null);
