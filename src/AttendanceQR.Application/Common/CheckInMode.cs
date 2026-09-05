namespace AttendanceQR.Application.Common;

/// <summary>
/// How ONE person checks in: whether their day needs a poster, and whether the radius refuses or
/// merely measures. The branch answers for everybody posted to it; a person may carry an exception.
///
/// It is one function on purpose. The scan path, the profile the phone reads and the admin screens
/// each have to answer the same two questions, and the moment they answer them separately a worker
/// gets a screen that offers a selfie and a server that demands a poster.
/// </summary>
public static class CheckInMode
{
    /// <summary>Is there a poster to scan? False = the home screen's «Giriş et» with a selfie.</summary>
    public static bool IsQrless(bool branchQrless, bool? employeeOverride) => employeeOverride ?? branchQrless;

    /// <summary>Does the radius REFUSE a scan from outside, or only record where it happened?</summary>
    public static bool IsFenced(bool branchRequiresFence, bool? employeeOverride) => employeeOverride ?? branchRequiresFence;
}
