namespace AttendanceQR.Api.Contracts;

/// <summary>Company-level "Ayın işçisi" settings. ManualFrom/ManualTo together override the
/// automatic last-N-days window; both null returns to the monthly rhythm.</summary>
public record VoteSettingsRequest(
    bool Enabled,
    int OpenDaysBeforeEnd,
    DateOnly? ManualFrom,
    DateOnly? ManualTo,
    int MinCandidates,
    int MinVotesToDecide);
