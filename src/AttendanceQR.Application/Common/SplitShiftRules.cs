namespace AttendanceQR.Application.Common;

/// <summary>
/// When a day that is worked in TWO stretches may open its second one.
///
/// The «əlavə qüvvə» crew at Fəvvarələr washes an area from 07:00 to 11:00, goes home, and comes back
/// at 22:00 until 07:00 the next morning. Both stretches fall on one calendar day, and until now the
/// system held one record per person per day: the 11:00 check-out closed the day, and the 22:00
/// arrival was refused as «artıq tamamlamısınız». Nine hours of night work were not merely
/// mis-measured — they were never recorded at all.
///
/// The rule below is deliberately the narrowest thing that solves that. It opens a second block only
/// when ALL of these hold, and the first is what confines the change to the crews it was written for:
///
///   1. the shift ITSELF declares a second window — an ordinary shift has none and can never reach
///      this path, so for the other six hundred people the scan endpoint behaves exactly as before,
///      «artıq tamamlamısınız» included;
///   2. the day's first block is finished — there is nothing open to check out of;
///   3. the day has not already had its two blocks, so a third arrival is still refused;
///   4. the clock is at or near the second window.
///
/// Point 4 is what stops the obvious accident. People scan repeatedly when GPS fails — at Aeroport
/// yolu, sixteen of ninety-three closed days last week were under an hour for exactly that reason — and
/// without a window test a stray afternoon retry would open a block that stayed open all night and was
/// closed by the next morning's arrival as a fourteen-hour phantom shift. That is the same cascade the
/// night-shift rules were written to stop; it must not come back through this door.
/// </summary>
public static class SplitShiftRules
{
    /// <summary>
    /// How early somebody may turn up for the second stretch and still have it read as that stretch.
    ///
    /// Two hours. A crew told to be back at 22:00 arrives at 21:30, and a shift that starts an hour
    /// early because the lorry came early is ordinary. Wider than this and an afternoon mis-tap starts
    /// qualifying, which is the accident point 4 exists to prevent.
    /// </summary>
    public const int EarlyToleranceHours = 2;

    /// <summary>The most stretches one day may hold. Two, because that is what a split day IS — a
    /// third arrival is a mis-scan, and refusing it keeps the old protection for the same person on
    /// the same day.</summary>
    public const int MaxBlocksPerDay = 2;

    /// <summary>
    /// Is this time inside a window, counting a window that crosses midnight as one stretch?
    ///
    /// 22:00–07:00 contains 23:00 and 03:00 and does not contain 08:00. Written as a wrap test rather
    /// than by adding a day to the end, because the caller has a time of day and not a moment — the
    /// same reason the overnight shift calculation uses a noon pivot rather than real dates.
    /// </summary>
    public static bool InWindow(TimeOnly at, TimeOnly start, TimeOnly end)
        => end > start
            ? at >= start && at < end
            : at >= start || at < end;   // crosses midnight

    /// <summary>
    /// May a scan now open the day's second stretch?
    /// </summary>
    /// <param name="nowLocal">Company-local time of day of this scan.</param>
    /// <param name="hasSecondWindow">Does the shift resolved FOR THIS DAY declare one? See
    /// <c>EffectiveShift.HasSecondWindow</c>. False for every ordinary shift, and for a split-shift
    /// crew on a day they were not moved onto the double shift.</param>
    /// <param name="secondStart">Start of the second stretch.</param>
    /// <param name="secondEnd">End of it — may be earlier than the start when it crosses midnight.</param>
    /// <param name="blocksToday">How many stretches this person already has on this date, open or closed.</param>
    /// <param name="anyOpen">Whether one of them is still open. An open block is checked OUT of, never
    /// added to — asking this here keeps the caller from having to order the two questions correctly.</param>
    public static bool MayOpenSecondBlock(
        TimeOnly nowLocal,
        bool hasSecondWindow,
        TimeOnly? secondStart,
        TimeOnly? secondEnd,
        int blocksToday,
        bool anyOpen)
    {
        if (!hasSecondWindow || secondStart is not TimeOnly start || secondEnd is not TimeOnly end)
            return false;
        if (anyOpen) return false;
        // Exactly one finished block: none means this is the ordinary first check-in and does not come
        // through here, and two means the day is done.
        if (blocksToday < 1 || blocksToday >= MaxBlocksPerDay) return false;

        return InWindow(nowLocal, start.AddHours(-EarlyToleranceHours), end);
    }
}
