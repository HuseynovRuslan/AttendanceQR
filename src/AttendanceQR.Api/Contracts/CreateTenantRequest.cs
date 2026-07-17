namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Stand up a new company. Everything except the slug and the admin's phone has a sensible default,
/// because this replaces a startup env-var block that had none and still worked.
/// </summary>
/// <param name="Slug">Becomes the hostname: &lt;slug&gt;.qrlog.az. Lowercase, 2–20 chars.</param>
/// <param name="AdminPin">Their first PIN, 4 digits. Omit and one is generated — either way they are
/// forced to replace it on first login, and it is shown once and never readable again.</param>
public record CreateTenantRequest(
    string Slug,
    string? DisplayName = null,
    string? AdminName = null,
    string? AdminPhone = null,
    string? AdminPin = null,
    string? LocationName = null,
    double? Latitude = null,
    double? Longitude = null,
    string? Color = null,
    string? LogoUrl = null);

/// <summary>Display name / accent colour / logo. A null field is left alone; an empty string clears
/// it back to the built-in default.</summary>
public record TenantBrandingRequest(
    string? DisplayName = null,
    string? Color = null,
    string? LogoUrl = null);
