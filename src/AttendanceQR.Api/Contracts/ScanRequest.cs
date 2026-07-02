namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Check-in/out scan payload. The employee identity now comes from the authenticated JWT
/// (the "sub" claim), not from the body.
/// </summary>
public record ScanRequest(
    string QrToken,
    string DeviceFingerprint,
    double Latitude,
    double Longitude);
