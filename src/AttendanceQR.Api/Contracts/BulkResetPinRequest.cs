namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Reissue temporary PINs to a group. Everyone named gets a NEW one, so any PIN already handed to
/// them stops working — this is not a way to read the old ones back, because there is none.
/// </summary>
public record BulkResetPinRequest(IReadOnlyList<Guid>? EmployeeIds);
