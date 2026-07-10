namespace AttendanceQR.Api.Contracts;

/// <summary>
/// A scan that was abandoned on the phone, before <c>/scan</c> was ever called — almost always
/// because the browser would not hand over a position. Nothing is written otherwise, so an employee
/// standing at the poster unable to check in leaves no trace at all in the admin panel.
/// </summary>
/// <param name="Reason">Must be one of the controller's allow-list; the body is employee-controlled.</param>
/// <param name="AccuracyMeters">Reported ± accuracy, when a position came back too coarse to trust.</param>
public record ScanFailureRequest(string Reason, double? AccuracyMeters = null);
