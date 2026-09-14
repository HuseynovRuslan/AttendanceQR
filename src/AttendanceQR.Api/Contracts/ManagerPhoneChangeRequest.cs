namespace AttendanceQR.Api.Contracts;

/// <summary>
/// The login number — the one field a manager may change on an account outside their own branch, and on
/// a fellow manager's. See ManagerController.ChangePhone for who qualifies and why nothing else rides here.
/// </summary>
public record ManagerPhoneChangeRequest(string? PhoneNumber);
