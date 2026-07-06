using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

/// <summary>Create an approved leave/permission range for an employee. Both dates inclusive.</summary>
public record LeaveRecordRequest(Guid EmployeeId, DateOnly FromDate, DateOnly ToDate, LeaveType Type, string? Note);
