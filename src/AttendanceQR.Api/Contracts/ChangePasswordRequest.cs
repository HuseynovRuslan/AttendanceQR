namespace AttendanceQR.Api.Contracts;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
