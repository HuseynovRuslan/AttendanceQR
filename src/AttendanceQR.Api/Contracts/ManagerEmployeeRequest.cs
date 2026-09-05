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
    // Whether this employee may use field/mobile check-in ("Sahə ziyarəti"). Defaults to false; every
    // updateManagerEmployee caller must send it, or a partial update turns it off.
    bool CanFieldCheckIn = false,
    bool CanShareDevice = false,
    // Round-tripped by the manager's form, never defaulted: null means "follow the branch", so an
    // omitted value must not read as a pinned exception. See Employee.QrlessCheckInOverride.
    bool? QrlessCheckInOverride = null,
    bool? RequireGeofenceOverride = null,
    // Shift assignment and rotation — see EmployeeUpdateRequest. A manager is usually the person who
    // actually knows who is on which shift, so they may set it for their own staff.
    Guid? ScheduleId = null,
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null,
    bool IsActive = true,
    // Structured name parts. When both are given, FullName is (re)composed as "FirstName LastName".
    string? FirstName = null,
    string? LastName = null);
