namespace AttendanceQR.Api.Contracts;

/// <summary>Replace the full set of recipients a given assigner is allowed to send tasks to.</summary>
public record SetTaskRecipientsRequest(Guid[] RecipientEmployeeIds);
