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

/// <summary>Commercial plan, soft limits and per-tenant feature flags. Null/zero limits = unlimited.
/// <c>DisabledFeatures</c> is the list of feature keys turned OFF (see TenantFeatures); omit/empty =
/// every feature on.</summary>
public record TenantPlanRequest(
    string? Plan = null,
    int? MaxEmployees = null,
    int? MaxLocations = null,
    string[]? DisabledFeatures = null,
    // Negotiated flat monthly price (AZN). Null clears the override → billing falls back to the formula.
    decimal? MonthlyPriceOverride = null);

/// <summary>Mark a company's bill for a period paid/unpaid. Year/Month default to the current month;
/// Amount defaults to the tenant's negotiated/ formula price.</summary>
public record BillingMarkRequest(
    int? Year = null,
    int? Month = null,
    bool IsPaid = false,
    decimal? Amount = null,
    string? Note = null);

/// <summary>Broadcast a platform-wide announcement to every company's employees.
/// <c>ScheduledForUtc</c> null = live immediately.</summary>
public record GlobalAnnouncementRequest(
    string? Title,
    string? Message,
    DateTime? ScheduledForUtc = null);

/// <summary>Set an operator's role — "Full" / "Support" / "Billing" (see OperatorRoleType).</summary>
public record OperatorRoleRequest(string? Role);
