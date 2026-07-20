namespace AttendanceQR.Api.Contracts;

/// <summary>Create an announcement. Audience is "All" | "AtWork" | "NotAtWork" | "Selected"
/// (RecipientIds required for Selected). ScheduledForLocal is a "yyyy-MM-ddTHH:mm" wall-clock time in
/// the app timezone; null = show immediately.</summary>
public record AnnouncementRequest(
    string Message,
    string? Title = null,
    string Audience = "All",
    string? ScheduledForLocal = null,
    IReadOnlyList<Guid>? RecipientIds = null);
