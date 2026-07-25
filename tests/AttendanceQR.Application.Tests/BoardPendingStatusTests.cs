using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The board's "not due yet" status. A scheduled worker whose shift hasn't started shows "Pending",
/// not the red "Absent" — fixing a board that could not tell a night worker whose shift is at 21:00
/// apart from someone who simply did not come.
/// </summary>
public class BoardPendingStatusTests
{
    private static Location Loc() => new()
    {
        Id = Guid.NewGuid(),
        ShiftStart = new TimeOnly(9, 0),
        ShiftEnd = new TimeOnly(18, 0),
        LateThresholdMinutes = 15,
        WorkDaysMask = 126,
    };

    private static EffectiveShift Night() =>
        EffectiveShift.Resolve(new TimeOnly(21, 0), new TimeOnly(7, 0), null, 1, null, null, Loc());

    private static EffectiveShift Day() =>
        EffectiveShift.Resolve(new TimeOnly(9, 0), new TimeOnly(18, 0), null, 1, null, null, Loc());

    private static string Status(EffectiveShift shift, int hour, int minute, bool isToday = true) =>
        ReportQueryService.BoardDisplayStatus(
            DailySummaryStatus.Absent, shift, isToday, new TimeOnly(hour, minute));

    [Theory]
    // Night shift 21:00–07:00 — pending all day until it is nearly due, then a real absence.
    [InlineData(10, 0, "Pending")]  // morning, shift is 11h away
    [InlineData(20, 0, "Pending")]  // just before start
    [InlineData(21, 10, "Pending")] // within the 15-min grace
    [InlineData(21, 30, "Absent")]  // past grace — genuinely a no-show for tonight
    public void NightShift_PendingUntilDue(int hour, int minute, string expected)
        => Assert.Equal(expected, Status(Night(), hour, minute));

    [Theory]
    // Day shift 09:00–18:00 — pending only in the pre-shift window.
    [InlineData(7, 0, "Pending")]   // before 09:00
    [InlineData(9, 10, "Pending")]  // within grace
    [InlineData(9, 30, "Absent")]   // past grace
    public void DayShift_PendingOnlyBeforeStart(int hour, int minute, string expected)
        => Assert.Equal(expected, Status(Day(), hour, minute));

    [Fact]
    public void PastDay_NeverPending_AnAbsenceStaysAnAbsence()
    {
        // A finished day: a no-show is a real absence, never "not due yet", whatever the hour shown.
        Assert.Equal("Absent", Status(Day(), 7, 0, isToday: false));
    }

    [Fact]
    public void OnlyAbsentBecomesPending_OtherStatusesPassThrough()
    {
        // The relabel is exactly for the would-be Qayıb. A day off, leave, or a present worker is
        // never touched — those are already unambiguous.
        var s = Day();
        foreach (var st in new[] { DailySummaryStatus.OnTime, DailySummaryStatus.DayOff, DailySummaryStatus.OnLeave, DailySummaryStatus.Incomplete })
            Assert.Equal(st.ToString(), ReportQueryService.BoardDisplayStatus(st, s, true, new TimeOnly(6, 0)));
    }
}
