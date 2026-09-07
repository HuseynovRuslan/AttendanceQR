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

    /// <summary>
    /// The second fingerprint: a DAY whose two scans cannot both belong to it.
    ///
    /// Arrival times alone miss the worst version of this failure, and missed it in production for a
    /// week. A night guard at Qala Anbar is on the branch's 09:00–18:00 because no shift was ever
    /// assigned to him, so the rule that closes a night with a morning scan — which needs the shift to
    /// cross midnight — never runs. His 08:00 exit therefore OPENS a new day, and his 20:00 arrival
    /// that evening CLOSES it. Every day is then stored as "in at 04:46, out at 22:01": seventeen
    /// hours, most of them spent asleep at home, and the night he actually worked is nowhere.
    ///
    /// Read as arrivals, those 04:46 and 05:21 scans sit less than four hours from a 09:00 start, so
    /// <see cref="IsOff"/> shrugged and the man stayed invisible while his rest days were scored
    /// «Qayıb» and deducted from his pay. What gives him away is not either scan but the SPAN between
    /// them: nobody is at work from before dawn until after dark, so the two scans belong to two
    /// different nights and the shift behind them is wrong.
    ///
    /// Deliberately three conditions, not one. Pre-dawn in and late-evening out alone would flag an
    /// honest long day; the span must also be implausible as a single shift (twelve hours) AND clearly
    /// longer than the shift the person is actually on — which is the thing this report asks about.
    /// </summary>
    public static bool IsSplitNight(TimeOnly checkIn, TimeOnly checkOut, TimeSpan shiftLength)
    {
        if (checkIn.Hour >= SplitNightInBefore || checkOut.Hour < SplitNightOutAfter)
            return false;

        var span = checkOut.ToTimeSpan() - checkIn.ToTimeSpan();
        return span >= TimeSpan.FromHours(SplitNightSpanHours)
            && span >= shiftLength + TimeSpan.FromHours(SplitNightLongerBy);
    }

    /// <summary>Before this hour, an arrival is too early to be the start of any day shift.</summary>
    public const int SplitNightInBefore = 7;

    /// <summary>From this hour, a departure is too late to be the end of the same day's shift.</summary>
    public const int SplitNightOutAfter = 19;

    /// <summary>A single shift this long does not happen; the two scans are two different nights.</summary>
    public const int SplitNightSpanHours = 12;

    /// <summary>And it has to exceed the assigned shift by this much, or it is merely a long day.</summary>
    public const int SplitNightLongerBy = 3;

    /// <summary>
    /// Two such days. One is a story — somebody stayed the night, a record was closed by hand. Two is
    /// a shift that is wrong, and this shape is specific enough that waiting for a majority (which
    /// <see cref="ShouldFlag"/> requires of arrivals) would leave the person hidden for another month.
    /// </summary>
    public const int MinSplitNightDays = 2;
}
