namespace AttendanceQR.Api.Contracts;

/// <summary>Admin correction of an existing record. Either field may be omitted to leave it as-is.</summary>
public record AdminAttendanceUpdateRequest(DateTime? CheckInAtUtc, DateTime? CheckOutAtUtc);

/// <summary>Admin-created record for a day the employee never scanned at all (e.g. a forgotten
/// device, a manual override). CheckInAtUtc is required — a "record" with no check-in isn't
/// meaningful; CheckOutAtUtc is optional (creates an Incomplete record, editable later).</summary>
public record AdminAttendanceCreateRequest(Guid EmployeeId, DateOnly Date, DateTime CheckInAtUtc, DateTime? CheckOutAtUtc);

/// <summary>
/// «Saxta giriş» — void one scan whose selfie is not the person it claims to be.
///
/// Every field is a separate decision, and none is implied by another:
///   • <paramref name="RevokeDevice"/> — on a shared brigade phone this locks out everyone who rides
///     on that handset, not just the one person, so it is never done automatically.
///   • <paramref name="NotifyEmployee"/> — the message is an accusation. Sending it is the admin's
///     call, not a side effect of correcting a day.
/// </summary>
/// <param name="Reason">In the admin's own words. Falls back to the face-mismatch wording.</param>
public record VoidFraudRequest(string? Reason = null, bool RevokeDevice = false, bool NotifyEmployee = true);

/// <summary>
/// The photo warning's text, when the admin writes their own rather than sending the template.
/// </summary>
/// <param name="Message">Free text, capped at 500 characters. Null or blank → the neutral template
/// («şəkil yoxlamadan keçmədi, növbəti dəfə papağı çıxarın»). Whatever is written here reaches the
/// employee's phone and their in-app inbox verbatim, and is copied into the audit line with the
/// sender's id beside it.</param>
public record PhotoWarningRequest(string? Message = null);
