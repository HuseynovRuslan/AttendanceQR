namespace AttendanceQR.Api.Contracts;

/// <summary>Admin correction of an existing record. Either field may be omitted to leave it as-is.</summary>
public record AdminAttendanceUpdateRequest(DateTime? CheckInAtUtc, DateTime? CheckOutAtUtc);

/// <summary>Admin-created record for a day the employee never scanned at all (e.g. a forgotten
/// device, a manual override). CheckInAtUtc is required — a "record" with no check-in isn't
/// meaningful; CheckOutAtUtc is optional (creates an Incomplete record, editable later).</summary>
public record AdminAttendanceCreateRequest(Guid EmployeeId, DateOnly Date, DateTime CheckInAtUtc, DateTime? CheckOutAtUtc);
