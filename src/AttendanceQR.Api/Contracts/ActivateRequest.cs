namespace AttendanceQR.Api.Contracts;

public record ActivateRequest(
    string ActivationToken,
    string Password,
    string DeviceFingerprint);
