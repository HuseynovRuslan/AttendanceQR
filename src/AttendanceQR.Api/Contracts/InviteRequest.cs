using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

public record InviteRequest(
    string FullName,
    string? Email,
    Guid LocationId,
    EmployeeRole Role,
    string? PhoneNumber = null,
    string? FatherName = null,
    string? Position = null,
    int? BirthYear = null);

/// <summary>Edit an existing employee's profile, role, location and enabled state.</summary>
public record EmployeeUpdateRequest(
    string FullName,
    string? Email,
    Guid LocationId,
    EmployeeRole Role,
    bool IsActive,
    string? PhoneNumber = null,
    string? FatherName = null,
    string? Position = null,
    int? BirthYear = null,
    // Optional per-employee work hours ("HH:mm"); empty/null → falls back to the location's shift.
    string? WorkStart = null,
    string? WorkEnd = null);
