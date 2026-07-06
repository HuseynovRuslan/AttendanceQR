using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An admin-approved absence for one employee over an inclusive date range (Vacation/Sick/Unpaid
/// leave, or a short excused Permission day). A day inside the range with no check-in reports as
/// OnLeave/Permission instead of Absent; a day inside the range WITH a check-in still reports
/// normally (showing up on a leave day is worked time, not overridden).
/// </summary>
public class LeaveRecord
{
    public LeaveRecord()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid EmployeeId { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public LeaveType Type { get; set; }

    public string? Note { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
