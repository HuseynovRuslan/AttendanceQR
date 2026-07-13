namespace AttendanceQR.Api.Contracts;

/// <summary>The caller's own reference selfie (the face-audit baseline), as a base64 data URL or bare
/// base64. Used by the first-login flow for temp-PIN accounts that never took an activation selfie.</summary>
public record ReferencePhotoRequest(string PhotoBase64);
