using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One pre-computed attendance summary per employee per day, produced by the nightly job (and the
/// manual admin trigger). Reports read from here rather than recomputing over raw records.
/// Uniqueness is enforced on (EmployeeId, SummaryDate).
/// </summary>
public class DailySummary : ITenantScoped
{
    public DailySummary()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid LocationId { get; set; }

    public DateOnly SummaryDate { get; set; }

    public DateTime? CheckInAtUtc { get; set; }

    public DateTime? CheckOutAtUtc { get; set; }

    // Check-out minus check-in; 0 when the day is incomplete or absent.
    public int WorkedMinutes { get; set; }

    public DailySummaryStatus Status { get; set; }

    // Minutes past the shift start, only when Status == Late; otherwise 0.
    public int LateMinutes { get; set; }

    // Minutes worked past the shift end, only when positive; otherwise 0.
    public int OvertimeMinutes { get; set; }
}
