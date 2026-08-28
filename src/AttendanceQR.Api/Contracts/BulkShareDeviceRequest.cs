namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Grant or withdraw the shared-phone permission for a list of employees.
/// </summary>
/// <param name="Allowed">
/// True grants, false withdraws. Explicit rather than a toggle: a bulk action over a filtered list
/// must not depend on what each row happened to be, or pressing it twice would leave the branch in
/// two different states with no way to tell which.
/// </param>
public record BulkShareDeviceRequest(IReadOnlyList<Guid>? EmployeeIds, bool Allowed);
