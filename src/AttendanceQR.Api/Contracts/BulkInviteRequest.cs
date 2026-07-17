using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Add many employees at once. Rows are validated independently — a bad row (duplicate phone, missing
/// name, unknown branch) is reported back without blocking the rest.
/// </summary>
/// <param name="LocationId">The branch for rows that do not name one themselves.</param>
/// <param name="Role">The role for rows that do not name one themselves.</param>
public record BulkInviteRequest(
    Guid LocationId,
    EmployeeRole Role,
    IReadOnlyList<BulkInviteRow> Rows);

/// <summary>
/// One employee to create. Everything the single-employee form collects is here, so a spreadsheet
/// import is not a lesser way to add someone.
/// </summary>
/// <param name="RoleName">
/// This row's own role, by name — "İşçi"/"Menecer"/"Admin" or the English enum names. Null/empty →
/// the batch's Role. Carried as a string because it comes from a spreadsheet cell someone typed.
/// </param>
/// <param name="LocationName">
/// This row's own branch, by name, matched case-insensitively against the tenant's branches.
/// Null/empty → the batch's LocationId. A name that matches nothing fails only that row.
/// </param>
public record BulkInviteRow(
    string FullName,
    string? PhoneNumber = null,
    string? Email = null,
    string? Position = null,
    string? FatherName = null,
    int? BirthYear = null,
    string? RoleName = null,
    string? LocationName = null);
