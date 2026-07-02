using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api.Contracts;

public record InviteRequest(
    string FullName,
    string Email,
    Guid LocationId,
    EmployeeRole Role);
