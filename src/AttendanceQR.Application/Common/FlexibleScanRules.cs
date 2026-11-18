namespace AttendanceQR.Application.Common;

/// <summary>
/// What a scan means for somebody on «Elastik qrafik» — a shift with no hours (Schedule.IsFlexible).
///
/// The fixed-shift scan decides from the timetable. A flexible person has none, so a scan is read against the SESSION
/// they have open, if any — and, because the same tap can mean «I am leaving» or «I am arriving and forgot to sign
/// out last time», against how long that session would have run:
/// <list type="bullet">
/// <item>No open session → this scan opens one. As many sessions a day as they like: 09:00–12:00 and 13:00–18:00 is
/// two sessions, eight hours, and the hour between them is not paid.</item>
/// <item>Open for up to <see cref="ConfirmAfterHours"/> → this scan closes it, whatever the calendar says. That is
/// the night: in at 22:00 on the 16th, out at 06:00 on the 17th is one eight-hour session filed under the 16th (the day
/// it started), never split and never counted twice.</item>
/// <item>Open for longer, up to <see cref="MaxSessionHours"/> → the scan could be either, and only the person knows.
/// The phone ASKS: «finishing the session you started at 18:00?» or «you forgot to sign out — a new arrival?».
/// A long guard night is closed; a forgotten afternoon is left open for the admin and a new session starts.
/// Guessing either way pays somebody for hours they did not work or not at all for hours they did.</item>
/// <item>Open since an earlier day for longer than <see cref="MaxSessionHours"/> → a forgotten check-out. It is LEFT
/// OPEN — no exit time is ever invented for it; the admin sees it as an open day and puts in the real time — and this
/// scan opens a new session.</item>
/// </list>
/// The same calendar day never gets that last answer: one day may hold only one OPEN session (the database says so),
/// so a long session from earlier today is always asked about instead.
///
/// The scan endpoint keeps every other guard it has for everybody: the five-minute double-tap cool-off, the
/// «are you leaving?» question within two hours of arriving, the offline de-duplication.
/// </summary>
public static class FlexibleScanRules
{
    /// <summary>
    /// The longest a session may have run and still be closed by a scan. Past this, a session opened on an earlier day
    /// is a forgotten check-out (the longest real night in the companies' history is a 14-hour guard night,
    /// 18:00–08:00, so 16 hours keeps it; yesterday morning's forgotten check-out, 24 hours back, is not).
    /// </summary>
    public const int MaxSessionHours = 16;

    /// <summary>
    /// Up to this, a scan closes the open session without asking — a day's work, or a night shift (22:00–06:00,
    /// 20:00–08:00). Beyond it, until <see cref="MaxSessionHours"/>, the person is asked.
    /// </summary>
    public const int ConfirmAfterHours = 12;

    public static readonly TimeSpan MaxSession = TimeSpan.FromHours(MaxSessionHours);
    public static readonly TimeSpan ConfirmAfter = TimeSpan.FromHours(ConfirmAfterHours);

    public enum Outcome
    {
        /// <summary>Nothing open (or only a forgotten session from an earlier day): open a new session.</summary>
        CheckIn,

        /// <summary>Close the open session at this scan.</summary>
        CheckOut,

        /// <summary>The scan is EARLIER than the open session began — an offline scan replayed out of order. It cannot
        /// close a session that had not started yet, and opening one before it would overlap it.</summary>
        OutOfOrder,

        /// <summary>The open session has run long enough that this scan may be its end OR a new arrival after a
        /// forgotten check-out. Only the person can say which; the phone asks.</summary>
        AskLongSession,
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
        var ran = scanUtc - openedAt;
        if (ran <= ConfirmAfter)
            return Outcome.CheckOut;
        if (openedOn < scanDate && ran > MaxSession)
            return Outcome.CheckIn;
        return Outcome.AskLongSession;
    }

    /// <summary>
    /// Could a scan still close this open session (directly, or after asking)? False only for a forgotten check-out —
    /// a session from an earlier day older than <see cref="MaxSessionHours"/>.
    /// </summary>
    public static bool ClosesSession(DateTime openCheckInUtc, DateOnly openDate, DateTime scanUtc, DateOnly scanDate)
        => openDate >= scanDate || scanUtc - openCheckInUtc <= MaxSession;

    /// <summary>
    /// Is this open session a forgotten check-out — one no scan will close any more, so only the admin can? Used by the
    /// screens and the morning reminder: a flexible night still running at 02:00 is NOT one.
    /// </summary>
    public static bool IsForgotten(DateTime openCheckInUtc, DateOnly openDate, DateTime nowUtc, DateOnly today)
        => !ClosesSession(openCheckInUtc, openDate, nowUtc, today);
}
