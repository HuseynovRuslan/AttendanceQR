namespace AttendanceQR.Api.Contracts;

/// <summary>Which opt-in capability a bulk grant is about.</summary>
public enum BulkPermission
{
    /// <summary>May be carried on a brigade's shared phone (Employee.CanShareDevice).</summary>
    ShareDevice = 0,

    /// <summary>May record a GPS field visit at a site with no QR poster (Employee.CanFieldCheckIn).</summary>
    FieldCheckIn = 1,
}

/// <summary>
/// Grant or withdraw one opt-in capability for a list of employees.
/// </summary>
/// <param name="Allowed">
/// True grants, false withdraws. Explicit rather than a toggle: a bulk action over a filtered list
/// must not depend on what each row happened to be, or pressing it twice would leave the branch in
/// two different states with no way to tell which.
/// </param>
public record BulkPermissionRequest(
    IReadOnlyList<Guid>? EmployeeIds,
    BulkPermission Permission,
    bool Allowed);

/// <summary>
/// Put a list of employees on one named shift — or take them all off it.
/// </summary>
/// <param name="ScheduleId">
/// The shift, or null to clear it. Clearing returns each person to the older behaviour: their own
/// WorkStart/WorkEnd if set, otherwise their branch's — see EffectiveShift.
/// </param>
public record BulkScheduleRequest(
    IReadOnlyList<Guid>? EmployeeIds,
    Guid? ScheduleId);
