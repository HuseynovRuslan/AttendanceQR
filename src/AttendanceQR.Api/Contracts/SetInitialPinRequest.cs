namespace AttendanceQR.Api.Contracts;

/// <summary>
/// First-time PIN set for an account still on a temporary PIN (MustChangePin). Auth required; no
/// current PIN is asked for, since the caller has just authenticated with the temp PIN.
/// </summary>
public record SetInitialPinRequest(string NewPin);
