namespace AttendanceQR.Api.Contracts;

/// <summary>Create or edit a month's "Ayın işçisi" ballot. Start and end must fall in the same month —
/// votes are filed by period.</summary>
public record VoteCampaignRequest(
    DateOnly StartsOn,
    DateOnly EndsOn,
    int MinCandidates,
    int MinVotesToDecide,
    /// <summary>Positions barred from being nominated. Empty means everyone is eligible.</summary>
    List<string>? ExcludedPositions);
