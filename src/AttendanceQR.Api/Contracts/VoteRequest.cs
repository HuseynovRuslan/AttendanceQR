namespace AttendanceQR.Api.Contracts;

/// <summary>Cast the caller's single "Ayın işçisi" vote for a colleague in their own branch.</summary>
public record VoteRequest(Guid CandidateEmployeeId);
