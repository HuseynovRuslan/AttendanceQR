namespace AttendanceQR.Api.Contracts;

/// <summary>Self-service PIN reset: prove it's really you with a live selfie (matched to your reference
/// photo) from a device already bound to your account, and get a fresh temporary PIN on the spot.
/// Tenant comes from the subdomain, not the body.</summary>
public record ForgotPinVerifyRequest(string Identifier, string DeviceFingerprint, string PhotoBase64);
