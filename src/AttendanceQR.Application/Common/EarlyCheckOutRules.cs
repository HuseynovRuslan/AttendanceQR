namespace AttendanceQR.Application.Common;

/// <summary>
/// When a scan soon after arriving is taken as LEAVING, and when it is taken as «did it work?».
///
/// A scan of the poster by somebody who is checked in is a check-out; that is the whole model, and the
/// five-minute cool-off (TooSoonToCheckOut) was the only thing standing between a nervous second tap and
/// a closed day. It was not enough, and the offline queue is where it failed. With no signal the phone
/// cannot show the server's verdict, so people who were not sure the first tap had worked tapped again
/// six, fifteen, forty minutes later. Replayed later, that second tap was not a duplicate — it was a
/// CHECK-OUT at 07:44, and the day that had started at 07:38 was over. The same evening their real exit
/// was refused as «artıq tamamlamısınız». Over thirty days, 35 offline days closed within an hour of
/// arriving (6.6% of offline days, four times the online rate), and at least eleven of those people
/// were still at work and tried to leave properly in the evening. Each one is a day's pay.
///
/// So a check-out this soon after arriving is taken only when the employee SAID so: the phone asks
/// «Siz artıq işdəsiniz — çıxırsınız?» and sends the answer. Without it the scan changes nothing — the
/// check-in stands and the day stays open for the real exit. That is safe in both directions: a retry
/// is harmless, and somebody who genuinely leaves early says «bəli» and is recorded as before.
/// </summary>
public static class EarlyCheckOutRules
{
    /// <summary>
    /// How long after arriving a check-out needs the employee's explicit «yes».
    ///
    /// Two hours. The damaged days sat between six minutes and an hour and a half after arrival; a real
    /// shift shorter than two hours is rare enough that one extra question on that day costs nothing.
    /// The phone uses the same number to decide when to ask while offline.
    /// </summary>
    public const int ConfirmWithinMinutes = 120;

    /// <summary>
    /// Does this check-out need the employee's confirmation before it may close the day?
    /// </summary>
    /// <param name="checkInUtc">When the open stretch began.</param>
    /// <param name="scanUtc">When this scan was taken — the phone's clock for an offline replay.</param>
    /// <param name="confirmed">The employee answered «bəli, çıxıram» on the phone.</param>
    public static bool NeedsConfirmation(DateTime? checkInUtc, DateTime scanUtc, bool confirmed)
        => !confirmed
           && checkInUtc is DateTime inAt
           && scanUtc - inAt < TimeSpan.FromMinutes(ConfirmWithinMinutes);
}
