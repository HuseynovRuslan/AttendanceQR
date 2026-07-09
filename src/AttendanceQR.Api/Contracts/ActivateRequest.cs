namespace AttendanceQR.Api.Contracts;

/// <param name="PhotoBase64">
/// Optional enrollment selfie (WebP/JPEG data URL or bare base64) taken deliberately during
/// activation. Stored as the employee's reference photo for face audit — a clean, front-facing
/// reference beats the silent first-check-in fallback. Optional: activation succeeds without it.
/// </param>
public record ActivateRequest(
    string ActivationToken,
    string Password,
    string DeviceFingerprint,
    string? DeviceLabel = null,
    string? PhotoBase64 = null);
