namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Check-in/out scan payload. The employee identity now comes from the authenticated JWT
/// (the "sub" claim), not from the body.
/// </summary>
/// <param name="PhotoBase64">
/// Optional check-in selfie for photo audit — a WebP data URL (or bare base64) captured silently by
/// the client. Optional by design: if the camera is unavailable it is omitted and check-in proceeds
/// without a photo. Only sent for check-in, never check-out.
/// </param>
public record ScanRequest(
    string QrToken,
    string DeviceFingerprint,
    double Latitude,
    double Longitude,
    string? PhotoBase64 = null);
