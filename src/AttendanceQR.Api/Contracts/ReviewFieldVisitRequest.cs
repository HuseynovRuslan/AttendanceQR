namespace AttendanceQR.Api.Contracts;

/// <summary>
/// A manager's verdict on finished field work: was the job resolved or not, and why.
/// </summary>
/// <param name="Ok">True = «həll olundu», false = «həll olunmadı».</param>
/// <param name="Note">Optional, and the only free text on a verdict. Trimmed and capped server-side —
/// it is shown on a board and typed by a person in a hurry.</param>
public record ReviewFieldVisitRequest(bool Ok, string? Note = null);
