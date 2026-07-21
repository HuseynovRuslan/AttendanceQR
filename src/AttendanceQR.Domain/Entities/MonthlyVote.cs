namespace AttendanceQR.Domain.Entities;

/// <summary>
/// Records THAT an employee voted this period — never who for. Its unique (period, voter) index is
/// what stops a second vote.
///
/// The split into two tables is the whole anonymity guarantee: nowhere in the database does a row
/// exist linking a voter to a candidate, so not even an operator with direct SQL access can work out
/// who voted for whom. A single table with both columns would have been simpler and would have made
/// the promise of a secret ballot a lie. Tenant-scoped.
/// </summary>
public class MonthlyVoteBallot : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>First day of the month being voted on.</summary>
    public DateOnly Period { get; set; }

    public Guid VoterEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The running count for one candidate in one branch for one period. Incremented as votes come in;
/// carries no link to any voter. Tenant-scoped.
/// </summary>
public class MonthlyVoteTally : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public DateOnly Period { get; set; }

    /// <summary>Voting is per branch — colleagues vote for someone they actually work with.</summary>
    public Guid LocationId { get; set; }

    public Guid CandidateEmployeeId { get; set; }

    public int Votes { get; set; }
}

/// <summary>
/// A settled result: the employee of the month for one branch. Written once the period closes so the
/// badge and the history survive independently of the tallies. Tenant-scoped.
/// </summary>
public class MonthlyWinner : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public DateOnly Period { get; set; }

    public Guid LocationId { get; set; }

    public Guid EmployeeId { get; set; }

    public int Votes { get; set; }

    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
}
