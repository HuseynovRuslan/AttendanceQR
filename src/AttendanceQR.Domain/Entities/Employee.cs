using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class Employee : ITenantScoped, IHasWorkCycle
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

    // Kept for backward compatibility (bulk import + rows entered before full dates existed). When
    // BirthDate is set it is the source of truth and BirthYear is kept in sync with its year.
    public int? BirthYear { get; set; }

    // Full date of birth (day/month/year). Optional. Preferred over BirthYear for display; enables
    // birthday greetings later. Null on older rows that only ever had a year.
    public DateOnly? BirthDate { get; set; }

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

    // The named shift ("növbə") this employee is on, when they are on one. Set → the schedule decides
    // their hours, working days and rotation outright, and the four fields below are not consulted.
    // Null → the older per-employee behaviour, which is what every existing employee has.
    //
    // One choice instead of four fields is the whole point: fixing the eight CleanFix night workers
    // whose hours were wrong meant eight separate edits, and would now mean one.
    //
    // AttendanceQR.Application EffectiveShift is the single place that resolves this. Nothing else
    // should read Schedule, Employee and Location hours and decide between them itself.
    public Guid? ScheduleId { get; set; }

    // Rotation ("növbə"). The location's weekly WorkDaysMask can only express patterns that repeat
    // every 7 days, so it cannot describe a rotation at all: "every other day" is a 2-day cycle, and
    // 2 does not divide 7 — the pattern drifts across the week (Mon/Wed/Fri/Sun, then Tue/Thu/Sat).
    // The mask also lives on the Location, so two people at one site could never be on opposite days,
    // which is the whole point of a rotation.
    //
    // A cycle is described by its length and how many days at the START of it are worked, anchored to
    // one date the employee is known to have been ON:
    //     bir gündən bir   → Days 2, OnDays 1
    //     sutka (24/48)    → Days 3, OnDays 1
    //     2 iş / 2 istirahət → Days 4, OnDays 2
    //
    // Null Days = no rotation: the location's weekly mask decides, exactly as it did before this
    // existed. That is the default, so every employee in production is untouched.
    //
    // Holidays (NonWorkingDay) still apply on top — a rotation replaces the weekly mask, not the
    // calendar. See AttendanceCalculator.IsWorkingDay, the single place this is interpreted.
    public int? WorkCycleDays { get; set; }

    public int WorkCycleOnDays { get; set; } = 1;

    public DateOnly? WorkCycleAnchor { get; set; }

    // Fixed monthly salary in AZN, for the payroll report. Null = not set (that employee is left out of
    // the money totals). The report starts from this figure and deducts a per-day share for each
    // unexcused absence — approved leave/permission are NOT deducted. Overtime is shown as hours only,
    // never auto-converted to money (fixed-salary overtime is paid separately, by hand).
    public decimal? MonthlySalary { get; set; }

    // Admin kill-switch: whether the account is enabled. Independent of activation.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    // Invitation/activation. Only the SHA256 hash of the activation token is stored —
    // the plaintext token is never persisted. Nulled out once the account is activated.
    public string? InvitationTokenHash { get; set; }

    public DateTime? InvitationExpiresUtc { get; set; }

    // Null until the employee completes activation (sets password + binds device).
    public DateTime? ActivatedAtUtc { get; set; }

    // "Son aktivlik": the last time the employee opened the app (their mobile home/menu loads their
    // profile on open). NOT a login timestamp — the app keeps them signed in for ~100 years, so a
    // real "login" is rare; this is how the admin sees who actually uses the app day to day. Updated
    // throttled (~15 min) on the profile endpoint. Null = has never opened the app.
    public DateTime? LastActiveAtUtc { get; set; }

    /// <summary>
    /// Skips the check-in selfie for this employee, by an admin's decision.
    ///
    /// Someone who genuinely objects to being photographed will not comply — they will point the
    /// camera at the ceiling, and that is worse than an exemption: the record looks verified, the
    /// audit fills with junk, and colleagues learn that opting out silently works. An explicit
    /// exemption keeps the refusal visible and deliberate, and leaves the audit meaningful for
    /// everyone else. Location and device binding still apply — only the photo is waived.
    /// </summary>
    public bool PhotoExempt { get; set; }

    /// <summary>
    /// When the employee accepted the data-processing notice (GPS, check-in selfie, work data).
    ///
    /// The digital stand-in for a signature: the app stores face + location + salary, which is
    /// personal — and biometric — data, so before it collects any of that the employee is shown what
    /// is collected and why and taps "Razıyam". Null means not yet accepted; the app blocks on the
    /// consent screen until it is set, and an admin can see who has and hasn't agreed.
    /// </summary>
    public DateTime? ConsentAcceptedAtUtc { get; set; }

    // One binding per browser storage context (Safari, the installed PWA, a spare phone). Empty
    // until the employee activates. Capped and least-recently-used-evicted — see DeviceBindingRules.
    public ICollection<DeviceBinding> DeviceBindings { get; set; } = new List<DeviceBinding>();

    // Photo audit: the employee's reference selfie (object key in MinIO), captured the first time a
    // check-in photo is available and kept indefinitely. A manager compares a day's check-in photo
    // against this by eye — there is no biometric/face-recognition processing anywhere.
    public string? ReferencePhotoKey { get; set; }

    public DateTime? ReferencePhotoTakenAtUtc { get; set; }
}
