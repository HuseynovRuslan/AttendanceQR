namespace AttendanceQR.Api.Contracts;

/// <summary>
/// «Bu işçi bu gün bu növbədə işlədi». A pointer to a Schedule, never a pair of times — hours typed
/// onto a day are a copy, and a copy drifts from the rota it was copied from.
/// </summary>
public record ShiftOverrideRequest(Guid EmployeeId, DateOnly Date, Guid ScheduleId, string? Note = null);
