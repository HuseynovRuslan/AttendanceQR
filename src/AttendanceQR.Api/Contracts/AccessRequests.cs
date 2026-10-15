using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

/// <summary>A role as the role editor saves it. Validated in the action — never by attributes on the record.</summary>
/// <param name="Permissions">Keys from AccessPermissions; an unknown key is refused, never dropped silently.</param>
public record AccessRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);

/// <summary>
/// One role to one person over one area — or, for branch scope, over several branches at once (a timekeeper
/// usually covers two or three). Each branch becomes its own assignment row, checked and audited on its own.
/// </summary>
/// <param name="Scope">Company, Structure or Branch.</param>
/// <param name="StructureId">Exactly when Scope is Structure.</param>
/// <param name="LocationIds">Exactly when Scope is Branch; 1–100 branches.</param>
public record AssignRoleRequest(
    Guid EmployeeId, Guid RoleId, AccessScopeType Scope, Guid? StructureId = null, IReadOnlyList<Guid>? LocationIds = null);
