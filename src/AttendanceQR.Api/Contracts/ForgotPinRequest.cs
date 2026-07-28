namespace AttendanceQR.Api.Contracts;

/// <summary>Anonymous "PIN-i unutdum" — the phone number or email the employee logs in with. The
/// tenant is taken from the subdomain, not the body.</summary>
public record ForgotPinRequest(string Identifier);
