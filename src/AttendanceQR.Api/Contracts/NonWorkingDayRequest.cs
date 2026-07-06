namespace AttendanceQR.Api.Contracts;

/// <summary>Declare a date as a non-working day. LocationId null = applies to every location.</summary>
public record NonWorkingDayRequest(DateOnly Date, string Description, Guid? LocationId);
