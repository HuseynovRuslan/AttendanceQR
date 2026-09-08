namespace AttendanceQR.Application.Common;

/// <summary>
/// What a scan can possibly MEAN when the person works nights.
///
/// Every other shift fits inside one calendar day, so the scan path can decide check-in from
/// check-out with one question: is there an open record for today? A night shift breaks that. One
/// working day spans two dates, and the consequence is not theoretical — it is the single most
/// expensive failure this product has had.
///
/// The shape of it: their night is closed by a morning scan. Any FURTHER morning scan has nothing
/// left to close, so it is read as an arrival and opens tomorrow. That stray sits there all day, and
/// when the person comes to work that evening their arrival is read as the stray's check-out —
/// «09:35 → 21:30», twelve hours they spent asleep at home — while the night they actually work is
/// never opened at all. The next morning's exit then finds nothing to close and opens another stray,
/// and it repeats. Measured across forty days: eleven night workers, roughly one such day every day
/// and a half.
///
/// Both rules below refuse to guess. Neither blocks a check-in: the first fires only once the
/// person's own check-out for that same morning is already recorded, and the second only rewrites a
/// check-in that could not have belonged to the shift now starting.
/// </summary>
public static class NightScanRules
{
    /// <summary>
    /// Before this hour a scan belongs to the night that is ending, not to a day that is starting.
    /// The scan path already pivots on noon for the same reason.
    /// </summary>
    public const int MorningBefore = 12;

    /// <summary>
    /// Is this morning scan a repeat of a check-out that already happened?
    ///
    /// True only when all three hold: the scan is in the morning, the shift they are on today crosses
    /// midnight, and their night was already closed EARLIER THE SAME MORNING. That last condition is
    /// what makes refusing it safe — somebody whose night was never recorded is not silenced by this,
    /// because there is no check-out of theirs to point at.
    /// </summary>
    /// <param name="nightClosedOnLocalDate">The local date their previous night's check-out was
    /// recorded on, or null when that night is still open or does not exist.</param>
    public static bool IsRepeatOfThisMorningsExit(
        TimeOnly nowLocal, DateOnly today, bool todayShiftIsOvernight, DateOnly? nightClosedOnLocalDate)
        => nowLocal.Hour < MorningBefore
           && todayShiftIsOvernight
           && nightClosedOnLocalDate == today;

    /// <summary>
    /// Is this scan the evening ARRIVAL of a night worker who has a stray day open from this morning?
    ///
    /// If so the scan must start the night, not close the stray. Narrow on purpose:
    ///   • the shift crosses midnight and starts in the afternoon or later — a night, not a long day;
    ///   • it has already started, so this is an arrival rather than someone hanging about;
    ///   • and the open record's check-in is from the MORNING, which no shift starting at 18:00 or
    ///     21:00 could have produced. An early arrival at 20:30 for a 21:00 start is not a morning
    ///     scan and never reaches here — it is an ordinary check-in, and its later scan an ordinary
    ///     check-out.
    /// </summary>
    public static bool IsEveningArrivalOverStrayDay(
        TimeOnly nowLocal, TimeOnly shiftStart, bool isOvernight, TimeOnly? openCheckInLocal)
        => isOvernight
           && shiftStart.Hour >= MorningBefore
           && nowLocal >= shiftStart
           && openCheckInLocal is TimeOnly opened
           && opened.Hour < MorningBefore;
}
