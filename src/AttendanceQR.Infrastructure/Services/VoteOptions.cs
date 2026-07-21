namespace AttendanceQR.Infrastructure.Services;

/// <summary>"Ayın işçisi" ballot settings, bound from the "Vote" configuration section. Configurable
/// rather than hard-coded so the window can be widened for a trial (or tuned later) without a code
/// change and redeploy.</summary>
public sealed class VoteOptions
{
    public const string SectionName = "Vote";

    /// <summary>How many days before the month ends voting opens. 3 = the last three days.</summary>
    public int OpenDaysBeforeEnd { get; set; } = 3;

    /// <summary>Below this many eligible colleagues a "secret" ballot isn't secret — who voted for whom
    /// is guessable — so the branch holds no vote at all.</summary>
    public int MinCandidates { get; set; } = 3;

    /// <summary>A branch needs at least this many votes before anyone is crowned. Three people out of
    /// fifty deciding "employee of the month" — and it being announced company-wide — devalues the
    /// award and stings whoever actually worked hardest; better to publish nothing that month.</summary>
    public int MinVotesToDecide { get; set; } = 5;
}
