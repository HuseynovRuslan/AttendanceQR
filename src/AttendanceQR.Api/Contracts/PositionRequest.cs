namespace AttendanceQR.Api.Contracts;

/// <summary>Create, adopt or rename a job title.</summary>
public record PositionRequest(string Name);

/// <summary>Move everyone on one title onto another and drop the first. How the duplicates that free
/// text left behind get cleaned up.</summary>
public record PositionMergeRequest(string From, string Into);
