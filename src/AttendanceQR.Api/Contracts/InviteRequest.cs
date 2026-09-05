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
    /// <summary>
    /// True to activate the account immediately with a temporary PIN, instead of handing back an
    /// activation link. Both paths already existed — bulk-import makes PIN accounts and bulk-invite
    /// makes links — but adding ONE person could only ever produce a link, which is the wrong half
    /// for the people this is used on: a link has to reach a phone that can open it, and the workers
    /// being added one at a time are the ones whose number was mistyped, who arrived mid-week, or who
    /// never got the first message. A PIN is read out loud.
    /// </summary>
    bool ActivateWithPin = false,
    // Optional per-employee work hours ("HH:mm") at creation — lets a schedule (day/night) be assigned
    // right away instead of only after activation. Empty/null → falls back to the location's shift.
    string? WorkStart = null,
    string? WorkEnd = null,
    // Shift assignment and rotation at creation — see EmployeeUpdateRequest.
    Guid? ScheduleId = null,
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null,
    // Whether this employee may use field/mobile check-in ("Sahə ziyarəti") from creation.
    bool CanFieldCheckIn = false,
    // Whether this employee's account may be carried on a brigade's shared phone. Off unless asked
    // for — see Employee.CanShareDevice for why the default matters.
    bool CanShareDevice = false,
    // Tri-state and NULLABLE, unlike the flags above: null is a real value here — "follow the
    // branch" — so it cannot double as "the caller forgot to send it". A caller that omits these
    // leaves the person following their branch, which is what everybody does; a caller that means to
    // pin an exception says so explicitly.
    bool? QrlessCheckInOverride = null,
    bool? RequireGeofenceOverride = null,
    // Structured name parts. When both are given, FullName is (re)composed as "FirstName LastName".
    string? FirstName = null,
    string? LastName = null);

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
    // Whether this employee may use field/mobile check-in ("Sahə ziyarəti"). Defaults to false — like
    // every field here, a caller that omits it turns it OFF. Every updateEmployee caller must send it.
    bool CanFieldCheckIn = false,
    // Whether this employee's account may be carried on somebody else's phone (a brigade's shared
    // handset). Defaults to false — like every field here, a caller that omits it turns it OFF, which
    // for this one means the person can no longer clock in on the shared phone. Send it everywhere.
    bool CanShareDevice = false,
    // Tri-state and NULLABLE, unlike the flags above: null is a real value here — "follow the
    // branch" — so it cannot double as "the caller forgot to send it". An omitted field leaves the
    // person on their branch's setting, which is where everybody sits; an exception is only ever
    // pinned by an explicit true/false. See Employee.QrlessCheckInOverride.
    bool? QrlessCheckInOverride = null,
    bool? RequireGeofenceOverride = null,
    // The named shift ("növbə") this employee is on. Set → it decides their hours, working days AND
    // rotation, and the three WorkCycle fields below are ignored. Null → the per-employee fields.
    Guid? ScheduleId = null,
    // Rotation, used only when ScheduleId is null. Null WorkCycleDays = no rotation and the location's
    // weekly work days decide. Days is the cycle length and OnDays how many of its first days are
    // worked: "bir gündən bir" is (2, 1), sutka (3, 1), "2 iş / 2 istirahət" (4, 2).
    int? WorkCycleDays = null,
    int? WorkCycleOnDays = null,
    DateOnly? WorkCycleAnchor = null,
    IReadOnlyList<Guid>? ManagedLocationIds = null,
    // Structured name parts. When both are given, FullName is (re)composed as "FirstName LastName".
    string? FirstName = null,
    string? LastName = null);
