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

    /// <summary>Below this many colleagues a branch holds no ballot — a "secret" vote among three
    /// people is not secret.</summary>
    public int MinCandidates { get; set; } = 3;

    /// <summary>A branch with fewer votes than this gets no winner announced.</summary>
    public int MinVotesToDecide { get; set; } = 5;

    /// <summary>Set once employees have been told voting opened, so the notice goes out exactly once.</summary>
    public DateTime? OpenedNotifiedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True while today falls inside the window.</summary>
    public bool IsOpenOn(DateOnly today) => today >= StartsOn && today <= EndsOn;
}
