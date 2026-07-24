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
    int? BirthYear = null,
    // Full date of birth (day/month/year). Preferred over BirthYear; the year is kept in sync from it.
    DateOnly? BirthDate = null,
    // Fixed monthly salary in AZN for the payroll report; null = not set.
    decimal? MonthlySalary = null,
    // Optional per-employee work hours ("HH:mm") at creation — lets a schedule (day/night) be assigned
    // right away instead of only after activation. Empty/null → falls back to the location's shift.
    string? WorkStart = null,
    string? WorkEnd = null,
    // Rotation ("növbə") at creation — see EmployeeUpdateRequest for the meaning of the three fields.
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null);

/// <summary>Edit an existing employee's profile, role, location and enabled state.</summary>
/// <param name="LocationId">Where this person WORKS — the geofence their own scans are checked against.</param>
/// <param name="ManagedLocationIds">
/// For a Manager: the branches they may SEE in the reports. Nothing to do with LocationId — a manager
/// clocks in at one branch and may oversee several others, or none of the ones they oversee.
///
/// Null means "leave as-is"; an empty list clears them. This is the only way to set them: until now
/// nothing outside DevController ever wrote ManagedLocations, so every manager in production saw an
/// empty panel — their scope was an empty list, and an empty list matches no branch.
/// Ignored unless Role is Manager; an Admin sees everything and an Employee only themselves.
/// </param>
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
    // Full date of birth (day/month/year). Preferred over BirthYear; the year is kept in sync from it.
    DateOnly? BirthDate = null,
    // Optional per-employee work hours ("HH:mm"); empty/null → falls back to the location's shift.
    string? WorkStart = null,
    string? WorkEnd = null,
    // Fixed monthly salary in AZN for the payroll report; null = not set.
    decimal? MonthlySalary = null,
    // Waives the check-in selfie for this employee. Defaults to false, so — like every field here —
    // a caller that omits it turns it OFF. Every updateEmployee caller must send it.
    bool PhotoExempt = false,
    // Rotation ("növbə"). Null WorkCycleDays = no rotation, the location's weekly work days decide.
    // Days is the cycle length and OnDays how many of its first days are worked, anchored to one date
    // the employee works: "bir gündən bir" is (2, 1), sutka (3, 1), "2 iş / 2 istirahət" (4, 2).
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null,
    IReadOnlyList<Guid>? ManagedLocationIds = null);
