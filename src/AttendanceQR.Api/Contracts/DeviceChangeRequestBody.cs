namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Employee-initiated device-change request. The employee identity comes from the JWT
/// ("sub" claim), never from the body — only the target fingerprint is supplied.
/// </summary>
public record DeviceChangeRequestBody(string NewDeviceFingerprint);
