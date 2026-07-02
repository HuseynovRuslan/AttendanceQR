namespace AttendanceQR.Api.Contracts;

public record LoginRequest(
    string Email,
    string Password);
