namespace AttendanceQR.Api.Contracts;

/// <summary>
/// What a manager may set on one of their own employees. Deliberately has NO salary and NO role
/// field: a manager cannot set either, so the shape simply does not carry them — there is nothing to
/// forget to ignore on the server.
/// </summary>
public record ManagerEmployeeRequest(
    string FullName,
    string? Email,
    string? PhoneNumber,
    string? FatherName,
    string? Position,
    Guid LocationId,
    int? BirthYear = null,
    DateOnly? BirthDate = null,
    string? WorkStart = null,
    string? WorkEnd = null,
    bool PhotoExempt = false,
    // Rotation ("növbə") — see EmployeeUpdateRequest. A manager is usually the person who actually
    // knows who is on which half of the cycle, so they can set it for their own staff.
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null,
    bool IsActive = true);
