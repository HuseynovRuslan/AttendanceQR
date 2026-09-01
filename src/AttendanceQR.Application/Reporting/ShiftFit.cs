namespace AttendanceQR.Application.Reporting;

/// <summary>
/// Does an employee's ACTUAL arrival time fit the shift they are assigned to?
///
/// Written after 2026-09-01, when a night worker at Heydər Əliyev Mərkəzi was found on a day shift.
/// Nothing was broken: every screen faithfully applied the hours it was given. But the hours were
/// wrong, and because everything downstream reads them, everything downstream was wrong together —
/// the check-out reminder fired at the wrong hour, the home screen told a man who had worked ten
/// hours that he had not checked in, and the night he did work was scored as zero.
///
/// A wrong shift is silent. Nobody gets an error; the day simply comes out empty, once, for one
/// person, in a company of two hundred. The only way it surfaces is to go looking — which is what
/// this is: the register of people whose scans and whose schedule disagree.
///
/// It ACCUSES NOTHING. A mismatch is a question ("is this person's shift right?"), not a finding
/// about the employee. Somebody covering another crew for a fortnight is a mismatch and is behaving
/// perfectly. That is also why this is never used to reject a scan or to dock anyone: it is a report.
/// </summary>
public static class ShiftFit
{
    /// <summary>
    /// How far from the shift's start a check-in has to be before it counts as "not this shift".
    ///
    /// Four hours. Deliberately wide: an hour or two early is ordinary (traffic, an early crew, a
    /// standing arrangement), and a report that fires on ordinary behaviour is one people learn to
    /// close. Four hours is no longer a variation on the same shift — it is a different shift. In the
    /// real case the gap was eleven.
    /// </summary>
    public const int OffByHours = 4;

    /// <summary>
    /// Below this many scans there is nothing to conclude. One odd arrival is a story — a delivery, a
    /// doctor, a day covering somebody. A pattern needs repetitions.
    /// </summary>
    public const int MinScans = 3;

    /// <summary>
    /// Hours between two times of day, the SHORT way round the clock.
    ///
    /// 23:00 and 01:00 are two hours apart, not twenty-two. Getting this wrong is the whole reason a
    /// night shift is hard to reason about, and a naive subtraction would flag every night worker in
    /// the company while missing the ones who are genuinely misfiled.
    /// </summary>
    public static TimeSpan Gap(TimeOnly actual, TimeOnly expected)
    {
        var forward = actual - expected;                       // always [0, 24h)
        var backward = TimeSpan.FromHours(24) - forward;
        return forward < backward ? forward : backward;
    }

    /// <summary>The same distance rounded to whole hours, FOR DISPLAY ONLY — see <see cref="IsOff"/>.</summary>
    public static int GapHours(TimeOnly actual, TimeOnly expected) => (int)Math.Round(Gap(actual, expected).TotalHours);

    /// <summary>
    /// Is this one arrival too far from the shift to belong to it?
    ///
    /// Compares exact minutes, NOT <see cref="GapHours"/>. Rounding first made three and a half hours
    /// into "4" and therefore into a mismatch — so the threshold people were told was four was really
    /// three and a half, and the report would have accused a crew that starts half an hour early.
    /// The number on screen is rounded; the judgement never is.
    /// </summary>
    public static bool IsOff(TimeOnly actual, TimeOnly expected)
        => Gap(actual, expected) >= TimeSpan.FromHours(OffByHours);

    /// <summary>
    /// Worth putting in front of somebody?
    ///
    /// The MAJORITY of arrivals have to be off, not merely several of them. A person who works their
    /// shift and covers a night once a month should not sit on this list forever — the thing being
    /// looked for is a schedule that is wrong every day, which is the shape of the failure this
    /// report exists for.
    /// </summary>
    public static bool ShouldFlag(int scans, int offScans)
        => scans >= MinScans && offScans * 2 > scans;
}
