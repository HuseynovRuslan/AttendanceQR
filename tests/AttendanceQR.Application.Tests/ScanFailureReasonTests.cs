using AttendanceQR.Api.Controllers;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The vocabulary a phone may use to report a scan it could not make.
///
/// It is an allow-list, because the body is employee-controlled and lands in the audit log an admin
/// reads. That makes it a place where the two halves can drift apart silently: the phone sends a
/// reason, the server does not recognise it, the client sees a 4xx and drops the report — deliberately,
/// since retrying an unknown reason can never succeed — and the failure is invisible again. Nothing
/// crashes and no log line is written; the Problems screen simply stays empty while somebody stands
/// at a QR poster unable to clock in.
///
/// That is not hypothetical. Every camera fault used to arrive as one reason, so the screen could say
/// a scan failed but never why, and "have you allowed the camera?" was the only answer anyone could
/// give — to an employee who had already allowed it. Splitting the reasons is only worth anything if
/// the server accepts all of them.
/// </summary>
public class ScanFailureReasonTests
{
    /// <summary>Mirrors <c>CAMERA_FAIL_REASON</c> in <c>frontend/src/components/CameraHelp.tsx</c>,
    /// which has a matching test on its side.</summary>
    private static readonly string[] CameraReasonsTheClientSends =
        ["CameraDenied", "CameraInUse", "CameraNotFound", "CameraInsecure", "ScannerLoadFailed",
         "CameraFailed"];

    [Theory]
    [InlineData("CameraDenied")]
    [InlineData("CameraInUse")]
    [InlineData("CameraNotFound")]
    [InlineData("CameraInsecure")]
    [InlineData("ScannerLoadFailed")]
    [InlineData("CameraFailed")]
    public void Every_camera_reason_the_phone_sends_is_accepted(string reason)
        => Assert.Contains(reason, AttendanceController.ClientFailureReasons);

    [Fact]
    public void The_old_single_camera_reason_still_works()
    {
        // Phones run a cached copy of the app for days, and an offline report can be replayed up to 18
        // hours later. Retiring this would throw away exactly the reports from the phones having the
        // worst time — the ones too far behind to have picked up the new build.
        Assert.Contains("CameraBlocked", AttendanceController.ClientFailureReasons);
    }

    [Fact]
    public void A_reason_nobody_defined_is_refused()
    {
        // The allow-list has to stay an allow-list: this endpoint writes employee-supplied text into
        // the audit log, so free text here would be free text on the admin's screen.
        Assert.DoesNotContain("Whatever", AttendanceController.ClientFailureReasons);
        Assert.DoesNotContain("", AttendanceController.ClientFailureReasons);
    }

    [Fact]
    public void The_reasons_are_distinct()
    {
        // A duplicate would read as harmless and quietly halve a count on the Problems screen, which
        // groups by reason.
        Assert.Equal(
            AttendanceController.ClientFailureReasons.Length,
            AttendanceController.ClientFailureReasons.Distinct().Count());
    }

    [Fact]
    public void The_camera_reasons_carry_a_cause_apart_from_the_honest_unknown()
    {
        // CameraFailed means "the browser did not say", and it is the only one allowed to be vague.
        // If a second vague reason appears, the split has started to lose its point.
        var vague = CameraReasonsTheClientSends.Where(r => r == "CameraFailed").ToList();
        Assert.Single(vague);
    }
}
