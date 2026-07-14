using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class Employee : ITenantScoped
{
    public Employee()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    public string FullName { get; set; } = string.Empty;

    // Optional profile fields shown in the admin employee list. Nullable so existing rows and
    // admin/manager accounts that don't need them are unaffected.
    public string? FatherName { get; set; }

    public string? Position { get; set; }

    public int? BirthYear { get; set; }

    public string Email { get; set; } = string.Empty;

    // Optional alternative login identifier. Stored NORMALIZED (PhoneNumbers.Normalize) so it matches
    // however it's typed. Unique when present; null for accounts created before phone login existed.
    public string? PhoneNumber { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    // True when the current PasswordHash is a temporary PIN the admin handed out (bulk import or a
    // PIN reset) — the employee is forced to set their own PIN on first login before anything else.
    // Cleared when they set their own PIN. Surfaced to the client as the "mcp" JWT claim.
    public bool MustChangePin { get; set; }

    // Embedded in every issued JWT as the "tv" claim and checked against this value on every
    // request (see Program.cs OnTokenValidated) — bumping it instantly invalidates every
    // previously issued token. There is no refresh-token flow in this app (JWTs are long-lived,
    // ~100 years), so this is how "log out all other sessions" is achieved: change-password bumps
    // it and returns a freshly issued token carrying the new value, so only that call's session
    // survives.
    public int TokenVersion { get; set; }

    public EmployeeRole Role { get; set; }

    public Guid LocationId { get; set; }

    // Optional per-employee work hours. When set, they override the location's ShiftStart/ShiftEnd for
    // this employee's late-arrival / early-departure detection (staff at one location can keep different
    // hours). Null → fall back to the location's shift. Time-of-day only; the date comes from the scan.
    public TimeOnly? WorkStart { get; set; }

    public TimeOnly? WorkEnd { get; set; }

    // Admin kill-switch: whether the account is enabled. Independent of activation.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    // Invitation/activation. Only the SHA256 hash of the activation token is stored —
    // the plaintext token is never persisted. Nulled out once the account is activated.
    public string? InvitationTokenHash { get; set; }

    public DateTime? InvitationExpiresUtc { get; set; }

    // Null until the employee completes activation (sets password + binds device).
    public DateTime? ActivatedAtUtc { get; set; }

    // One binding per browser storage context (Safari, the installed PWA, a spare phone). Empty
    // until the employee activates. Capped and least-recently-used-evicted — see DeviceBindingRules.
    public ICollection<DeviceBinding> DeviceBindings { get; set; } = new List<DeviceBinding>();

    // Photo audit: the employee's reference selfie (object key in MinIO), captured the first time a
    // check-in photo is available and kept indefinitely. A manager compares a day's check-in photo
    // against this by eye — there is no biometric/face-recognition processing anywhere.
    public string? ReferencePhotoKey { get; set; }

    public DateTime? ReferencePhotoTakenAtUtc { get; set; }
}
