namespace AttendanceQR.Api.Contracts;

/// <summary>Create an announcement broadcast to every employee in the tenant.</summary>
public record AnnouncementRequest(string Message);
