using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// In the T-13 tabel, a recorded leave beats the «aktivləşdirməyib» guard.
///
/// Both rules say "do not judge this day", and they can hold at once: somebody hired mid-August, on
/// annual leave since the 3rd, who has therefore never scanned. When they disagree the sheet must
/// print the one a person asserted — «Məzuniyyət» is a fact an admin entered, «aktivləşdirməyib» is
/// an inference drawn from silence.
///
/// It printed the inference. A man on leave from 3 August to 6 September, activated on the 25th and
/// so still inside the fourteen-day grace, showed «–» on every cell of his holiday. His employer had
/// recorded the leave and the timesheet denied it.
///
/// The precedence is what is pinned here, in the same order the tabel loop applies it.
/// </summary>
public class TabelLeaveBeatsOnboardingTests
{
    private const string CodeNotActivated = "–";
    private const string CodeVacation = "M";
    private const string CodeSick = "X";

    /// <summary>The tabel's rule, as the loop now runs it: leave first, then the onboarding guard.</summary>
    private static string CellFor(bool onboarding, string? leaveCode) =>
        leaveCode is not null ? leaveCode
        : onboarding ? CodeNotActivated
        : "Q";

    [Fact]
    public void A_recorded_holiday_prints_M_even_while_still_onboarding()
    {
        Assert.Equal(CodeVacation, CellFor(onboarding: true, leaveCode: CodeVacation));
    }

    [Fact]
    public void The_same_holds_for_every_other_leave_type()
    {
        // Sick leave off a different budget, a business trip that is WORK — none of them may be
        // overwritten by a guard about whether the person has a phone yet.
        Assert.Equal(CodeSick, CellFor(onboarding: true, leaveCode: CodeSick));
        Assert.Equal("Ez", CellFor(onboarding: true, leaveCode: "Ez"));
    }

    [Fact]
    public void With_no_leave_the_onboarding_guard_still_wins()
    {
        // The guard's own job is untouched: it exists so the tabel does not resurrect the absences
        // the onboarding rule cleared, and a day nobody recorded anything for is still «–».
        Assert.Equal(CodeNotActivated, CellFor(onboarding: true, leaveCode: null));
    }

    [Fact]
    public void And_an_ordinary_day_is_unaffected_by_either()
    {
        Assert.Equal("Q", CellFor(onboarding: false, leaveCode: null));
        Assert.Equal(CodeVacation, CellFor(onboarding: false, leaveCode: CodeVacation));
    }
}
