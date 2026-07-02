using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class AttendanceRecord
{
    public AttendanceRecord()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid LocationId { get; set; }

    // Per-day uniqueness key — see (EmployeeId, AttendanceDate) unique index.
    public DateOnly AttendanceDate { get; set; }

    public DateTime? CheckInAtUtc { get; set; }

    public DateTime? CheckOutAtUtc { get; set; }

    public AttendanceStatus Status { get; set; }
}
