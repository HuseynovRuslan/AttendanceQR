namespace AttendanceQR.Api.Contracts;

/// <summary>Create a task and send it to one or more recipients (one TaskItem per recipient).
/// Title and DueDate required; Description optional.</summary>
public record CreateTaskRequest(Guid[] AssignedToEmployeeIds, string Title, string? Description, DateOnly? DueDate);
