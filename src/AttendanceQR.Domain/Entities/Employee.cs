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

    // Canonical display name, kept as "Ad Soyad". Still the field the whole app reads, searches and
    // sorts by — FirstName/LastName below are the structured source it is composed from on write, so
    // no query, report or export had to change. Backfilled from the old FullName by surname suffix.
    public string FullName { get; set; } = string.Empty;

    // Structured name parts. Nullable because older rows (and system accounts) predate the split; the
    // employee form writes all three, and FullName is set to "FirstName LastName" whenever they are.
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

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

    // Optional — an employee may sign in by phone alone. Null when none was given (no more
    // synthesised "emp-…@baki.local" placeholders). Unique per tenant only where present.
    public string? Email { get; set; }

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
    /// Whether this employee may use field/mobile check-in ("Səyyar / Sahə ziyarəti") — GPS attendance
    /// for ad-hoc sites with no QR poster. Off by default: only workers an admin actually sends into the
    /// field see the menu row + home self-report and can be assigned a visit; a plain office employee
    /// never sees any of it. Enforced server-side on self-report + assign, not merely hidden in the UI.
    /// </summary>
    public bool CanFieldCheckIn { get; set; }

    /// <summary>
    /// Whether this employee's account may be carried on a device that already belongs to someone
    /// else — a brigade's shared phone, for the ~260 workers who have no phone of their own.
    ///
    /// Off by default, and the default is the point. One phone, one employee is what stops a colleague
    /// recording an absent worker's arrival, and the shared-phone feature removes it for whoever is on
    /// the device. Without this flag ANY of the 114 employees could quietly assemble a shared phone —
    /// the company's own control weakened by a decision the company never made and could not see.
    ///
    /// It is enforced when a device is ADOPTED, not on every scan: a shared phone that already carries
    /// an employee keeps working, so turning this on does not strand anyone mid-morning. What it stops
    /// is a NEW account being added to somebody else's device.
    ///
    /// The two remaining controls are untouched and are the stronger pair anyway — the scan must happen
    /// inside the geofence, and the selfie must be this employee's face.
    /// </summary>
    public bool CanShareDevice { get; set; }

    /// <summary>
    /// This ONE person checks in without a poster, whatever their branch does. Null — the normal
    /// state — means the branch decides (<see cref="Location.QrlessCheckIn"/>).
    ///
    /// The branch is still where this belongs by default, and for a reason worth keeping in mind: the
    /// nineteen people who spent five weeks invisible were not missing a per-person permission, they
    /// had it. What was missing was anything about the PLACE telling the app there was no poster. So
    /// this is an exception, not the mechanism — for the driver whose branch has a poster he is never
    /// near, or the one person moved onto a poster-less patch before the whole branch is.
    ///
    /// Deliberately NOT <see cref="CanFieldCheckIn"/>: a field visit is an errand somewhere else, with
    /// its own screen, its own board and no fence. This is their ORDINARY day, recorded as an
    /// ordinary attendance row at their own branch.
    /// </summary>
    public bool? QrlessCheckInOverride { get; set; }

    /// <summary>
    /// This one person's scans are MEASURED rather than refused when they fall outside the radius.
    /// Null means the branch decides (<see cref="Location.RequireGeofence"/>).
    ///
    /// Same shape and same warning as above, one notch more dangerous: with it set to false this
    /// person can record a day from anywhere. It is written down every time (an audit row carrying
    /// the distance) and it is meant to come off once the site's real radius is known.
    /// </summary>
    public bool? RequireGeofenceOverride { get; set; }

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

    // Photo audit: the employee's reference selfie (object key in R2), captured the first time a
    // check-in photo is available and kept indefinitely.
    //
    // ⚠️ This IS biometric processing. A manager can compare a day's photo against this by eye, but
    // the FaceMatchWorker also sends both images to AWS Rekognition CompareFaces and stores a
    // similarity score (Employee → AttendanceRecord.FaceMatchScore/FaceMatchStatus). This comment
    // used to claim "there is no biometric/face-recognition processing anywhere", which was false
    // and is exactly the sort of statement a customer's reviewer checks. Anything written about how
    // this data is handled — privacy notice, DPA, consent basis — has to start from that fact.
    public string? ReferencePhotoKey { get; set; }

    public DateTime? ReferencePhotoTakenAtUtc { get; set; }

    /// <summary>
    /// PROFİL ŞƏKLİ — a picture the employee chose for themselves, shown on their own profile and in
    /// the account switcher on a shared handset.
    ///
    /// Deliberately NOT <see cref="ReferencePhotoKey"/>, and the distinction is the whole point. That
    /// one is a face-audit baseline: it is captured for comparison, it is fed to Rekognition, and it
    /// is only ever shown where there is a reason to inspect a face. This one is chosen, it is never
    /// compared to anything, it never reaches the face-match worker, and it exists for one reason —
    /// a crew phone holding thirty accounts shows thirty pairs of initials, and "Məmmədov Elçin" and
    /// "Məmmədov Elvin" are both ME. Tapping the wrong row files the wrong person's attendance.
    ///
    /// Own prefix (<c>avatars/</c>) so the retention job, which prunes check-in selfies, cannot reach
    /// it and the face-match worker never lists it. Null means show initials.
    /// </summary>
    public string? AvatarPhotoKey { get; set; }

    /// <summary>When the avatar was last set. The client caches the image against this, so it fetches
    /// a presigned URL once per change rather than on every profile open.</summary>
    public DateTime? AvatarUpdatedAtUtc { get; set; }
}
