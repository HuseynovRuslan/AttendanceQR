using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An employee's request to set the check-out time on a past day they forgot to scan out. A
/// manager/admin approves (writing the record's CheckOutAtUtc) or rejects. Deterrents against leaning
/// on this instead of scanning out: a required reason, a per-month cap, and the count is shown to the
/// approver and back to the employee.
/// </summary>
public class MissedCheckoutRequest
{
    public MissedCheckoutRequest()
    {
        Id = Guid.NewGuid();
        RequestedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // The employee who forgot to check out.
    public Guid EmployeeId { get; set; }

    // The open AttendanceRecord this request will close.
    public Guid AttendanceRecordId { get; set; }

    public DateOnly AttendanceDate { get; set; }

    // The check-out time the employee claims (UTC).
    public DateTime RequestedCheckOutAtUtc { get; set; }

    // Why they didn't scan out — required. A preset chip ("Yadımdan çıxdı") or a short free text.
    public string Reason { get; set; } = string.Empty;

    public MissedCheckoutStatus Status { get; set; } = MissedCheckoutStatus.Pending;

    public DateTime RequestedAtUtc { get; set; }

    // The Manager/Admin who reviewed it — null while pending.
    public Guid? ReviewedByEmployeeId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }
}
