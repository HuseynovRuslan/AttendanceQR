namespace AttendanceQR.Application.Common;

/// <summary>
/// What a scan means for somebody on «Elastik qrafik» — a shift with no hours (Schedule.IsFlexible).
///
/// The fixed-shift scan decides from the timetable: a morning scan closes last night only if last night's shift
/// crossed midnight, a second arrival is allowed only inside a declared second window, and so on. A flexible person
/// has no timetable, so all of that is replaced by one question about the SESSION they have open, if any:
/// <list type="bullet">
/// <item>No open session → this scan opens one. As many sessions a day as they like: 09:00–12:00 and 13:00–18:00 is
/// two sessions, eight hours, and the hour between them is not paid (they were not working).</item>
/// <item>A session opened on the SAME calendar day → this scan closes it, however long it ran.</item>
/// <item>A session opened on an EARLIER day and still within <see cref="MaxSessionHours"/> → this scan closes it. That
/// is the night: in at 22:00 on the 16th, out at 06:00 on the 17th is one eight-hour session, filed under the 16th
/// (the day it started), never split and never counted twice.</item>
/// <item>A session from an earlier day older than that → it was a forgotten check-out. It is LEFT OPEN — no exit time is
/// ever invented for it; it shows as an open day to the admin, who corrects it with the real time — and this scan opens
/// today's session. Closing it would have paid the person for the whole night they spent at home.</item>
/// </list>
/// The scan endpoint keeps every other guard it has for everybody: the five-minute double-tap cool-off, the
/// «are you leaving?» question within two hours of arriving, the offline de-duplication.
/// </summary>
public static class FlexibleScanRules
{
    /// <summary>
    /// The longest a session may have run and still be closed by a scan on a LATER day. Long enough for any real night
    /// (the longest in the companies' history is a 14-hour guard night, 18:00–08:00); short enough that yesterday
    /// morning's forgotten check-out is never closed by this morning's arrival (24 hours apart).
    /// </summary>
    public const int MaxSessionHours = 16;

    public static readonly TimeSpan MaxSession = TimeSpan.FromHours(MaxSessionHours);

    public enum Outcome
    {
        /// <summary>Nothing open (or only a stale session): open a new session for the scan's day.</summary>
        CheckIn,

        /// <summary>Close the open session at this scan.</summary>
        CheckOut,

        /// <summary>The scan is EARLIER than the open session began — an offline scan replayed out of order. It cannot
        /// close a session that had not started yet, and opening one before it would overlap it.</summary>
        OutOfOrder,
    }

    /// <summary>
    /// What a scan at <paramref name="scanUtc"/>, filed under <paramref name="scanDate"/>, does with the person's most
    /// recent open session — null when they have none.
    /// </summary>
    public static Outcome Decide(DateTime? openCheckInUtc, DateOnly? openDate, DateTime scanUtc, DateOnly scanDate)
    {
        if (openCheckInUtc is not DateTime openedAt || openDate is not DateOnly openedOn)
            return Outcome.CheckIn;
        if (scanUtc < openedAt)
            return Outcome.OutOfOrder;
        return ClosesSession(openedAt, openedOn, scanUtc, scanDate) ? Outcome.CheckOut : Outcome.CheckIn;
    }

    /// <summary>Would a scan close this open session, rather than leave it as a forgotten check-out?</summary>
    public static bool ClosesSession(DateTime openCheckInUtc, DateOnly openDate, DateTime scanUtc, DateOnly scanDate)
        => openDate >= scanDate || scanUtc - openCheckInUtc <= MaxSession;

    /// <summary>
    /// Is this open session a forgotten check-out — one no scan will close any more, so only the admin can? Used by the
    /// screens and the morning reminder: a flexible night still running at 02:00 is NOT one.
    /// </summary>
    public static bool IsForgotten(DateTime openCheckInUtc, DateOnly openDate, DateTime nowUtc, DateOnly today)
        => !ClosesSession(openCheckInUtc, openDate, nowUtc, today);
}
