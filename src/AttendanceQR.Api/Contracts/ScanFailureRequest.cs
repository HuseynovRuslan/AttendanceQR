namespace AttendanceQR.Api.Contracts;

/// <summary>
/// A scan that was abandoned on the phone, before <c>/scan</c> was ever called — almost always
/// because the browser would not hand over a position. Nothing is written otherwise, so an employee
/// standing at the poster unable to check in leaves no trace at all in the admin panel.
/// </summary>
/// <param name="Reason">Must be one of the controller's allow-list; the body is employee-controlled.</param>
/// <param name="AccuracyMeters">Reported ± accuracy, when a position came back too coarse to trust.</param>
/// <param name="ScanAtUtc">
/// WHEN the scan was taken, when that is not now — an offline scan can be reported days later, and
/// the audit row's own timestamp is the moment it was reported, not the day that was lost. Without
/// this an admin is told "a day went missing" and has no way to learn which day, which makes the
/// report unactionable: the only remedy is a manual attendance entry, and that needs a date.
/// </param>
public record ScanFailureRequest(string Reason, double? AccuracyMeters = null, DateTime? ScanAtUtc = null);
