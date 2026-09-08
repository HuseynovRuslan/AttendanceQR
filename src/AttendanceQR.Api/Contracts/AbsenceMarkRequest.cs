namespace AttendanceQR.Api.Contracts;

/// <summary>«Qayıb yaz» — one person, one day, and optionally why.</summary>
public record AbsenceMarkRequest(Guid EmployeeId, DateOnly Date, string? Note = null);
