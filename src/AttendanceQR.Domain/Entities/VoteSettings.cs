namespace AttendanceQR.Domain.Entities;

/// <summary>
/// Per-company "Ayın işçisi" settings, edited from the admin panel. One row per tenant.
///
/// These were server config (env vars) — which meant one setting for every company on the box, and a
/// redeploy to change any of it. A cleaning firm and a petrol depot have no reason to run the same
/// ballot window, and the owner shouldn't need the developer to move a date.
/// </summary>
public class VoteSettings : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>Master switch — off means the voting screen simply isn't offered.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Automatic window: voting opens this many days before the month ends.</summary>
    public int OpenDaysBeforeEnd { get; set; } = 3;

    /// <summary>Explicit window. When BOTH are set they override the automatic one — for a one-off
    /// round on chosen dates. Cleared to go back to the monthly rhythm.</summary>
    public DateOnly? ManualFrom { get; set; }

    public DateOnly? ManualTo { get; set; }

    /// <summary>Below this many colleagues a branch holds no ballot — a "secret" vote among three
    /// people is not secret.</summary>
    public int MinCandidates { get; set; } = 3;

    /// <summary>A branch with fewer votes than this gets no winner announced that month.</summary>
    public int MinVotesToDecide { get; set; } = 5;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
