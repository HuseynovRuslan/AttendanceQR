namespace AttendanceQR.Api.Contracts;

/// <summary>An employee's request to close a forgotten-checkout day: which record, the claimed
/// check-out time (UTC), and a required reason.</summary>
public record MissedCheckoutRequestBody(Guid RecordId, DateTime CheckOutAtUtc, string Reason);
