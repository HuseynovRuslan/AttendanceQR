using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api;

/// <summary>
/// The owner's rule of 2026-10-15: a correction to somebody's attendance says why, and the why is kept —
/// in the audit line written beside the change. Validated by hand, not by an attribute on the request
/// record: a validation attribute on a record's positional property once turned every scan into a 500
/// (see the record-validation note), and these requests sit next to the scan path.
/// </summary>
public static class CorrectionReason
{
    public const int Min = 3;
    public const int Max = 500;
    public const string Required = "ReasonRequired";

    /// <summary>The reason as it will be stored, or null when it is missing, too short or too long.</summary>
    public static string? Clean(string? reason)
    {
        var trimmed = reason?.Trim();
        return trimmed is { Length: >= Min and <= Max } ? trimmed : null;
    }

    /// <summary>
    /// The audit line of a correction that has no attendance row of its own to name — an absence mark, a
    /// missed check-out decision, a voided day restored. Same event and same shape as the record edits:
    /// who, what, and why in their own words, so one search finds every correction to a person's days.
    /// </summary>
    public static AuditLog Audit(Guid employeeId, Guid actorId, string? ip, string what, string reason)
    {
        var text = $"By {actorId}: {what}. Səbəb: {reason}";
        return new AuditLog
        {
            EmployeeId = employeeId,
            EventType = AuditEventType.RecordEditedByAdmin,
            Reason = text.Length <= 1000 ? text : text[..1000],
            IpAddress = ip,
        };
    }
}
