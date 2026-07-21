namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One "Ayın işçisi" ballot that an admin deliberately created for a month.
///
/// This replaced an always-on window computed from settings. The difference matters: a company may
/// simply not run the award some months, and with an automatic window nobody decides that — it opens
/// on its own, employees get asked to vote, and a winner is announced whether or not anyone meant to
/// hold one. No campaign row for a month means no ballot that month, full stop.
///
/// The thresholds live here rather than in company settings so a quiet month can be run with
/// different rules from a busy one, and so changing them later never rewrites a past result.
/// Tenant-scoped; one campaign per month.
/// </summary>
public class VoteCampaign : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>The month being voted on — first day. Unique per tenant.</summary>
    public DateOnly Period { get; set; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    /// <summary>Local time of day the ballot opens on <see cref="StartsOn"/>. Defaults to midnight.</summary>
    public TimeOnly StartsAt { get; set; } = new(0, 0);

    /// <summary>
    /// Local time of day the ballot closes on <see cref="EndsOn"/>. Defaults to one minute to midnight.
    ///
    /// Whole days were too blunt: a vote that closes "on the 31st" is still open during the shift on
    /// which the winner is announced, and a vote meant to run through one evening meeting had to be
    /// given the entire day.
    /// </summary>
    public TimeOnly EndsAt { get; set; } = new(23, 59);

    /// <summary>Below this many colleagues a branch holds no ballot — a "secret" vote among three
    /// people is not secret.</summary>
    public int MinCandidates { get; set; } = 3;

    /// <summary>A branch with fewer votes than this gets no winner announced.</summary>
    public int MinVotesToDecide { get; set; } = 5;

    /// <summary>
    /// Positions that cannot be nominated — typically leadership, who would win on standing rather
    /// than on the month's work.
    ///
    /// Deliberately a list of who is OUT, not who is IN: Position is free text and may be empty, so an
    /// allow-list would silently drop every employee with no position set and every newly-typed one.
    /// Empty (the default) means everyone at the branch is a candidate.
    /// </summary>
    public List<string> ExcludedPositions { get; set; } = new();

    /// <summary>Set once employees have been told voting opened, so the notice goes out exactly once.</summary>
    public DateTime? OpenedNotifiedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Opening moment in the company's local time.</summary>
    public DateTime OpensAtLocal => StartsOn.ToDateTime(StartsAt);

    /// <summary>Closing moment in the company's local time.</summary>
    public DateTime ClosesAtLocal => EndsOn.ToDateTime(EndsAt);

    /// <summary>True while the given local moment falls inside the window.</summary>
    public bool IsOpenAt(DateTime localNow) => localNow >= OpensAtLocal && localNow <= ClosesAtLocal;
}
