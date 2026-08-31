using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The live board's decision to carry a still-open record from yesterday onto today.
///
/// This exists because the first version of that carry-over was too broad: it moved ANY open record
/// forward, so a day worker who forgot to check out yesterday showed up "İşdə" on today's board with
/// yesterday's check-in time — a time that then read as being in the future. A real report:
/// "Şirin Eyvazova · İşdə 10:58" while the clock said 09:10. She is a day worker; her 10:58 was
/// yesterday's, and it should never have been on today's board.
///
/// Two guards decide it, both pinned here: the shift must be overnight, and it must still be within
/// the shift's window (up to its end time + 2h of grace).
/// </summary>
public class BoardCarryOverTests
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

    [Fact]
    public void DayShift_IsNotOvernight_SoItIsNeverCarriedOver()
    {
        // The whole Şirin bug in one assertion: a day shift is not overnight, and the carry-over is
        // gated on IsOvernight before the time window is even consulted.
        Assert.False(Day().IsOvernight);
        Assert.True(Night().IsOvernight);
    }

    [Theory]
    [InlineData(0, 0, true)]   // 00:00 — deep in the night, shift plainly still running
    [InlineData(6, 30, true)]  // 06:30 — before the 07:00 end
    [InlineData(7, 0, true)]   // 07:00 — exactly at end
    [InlineData(8, 59, true)]  // 08:59 — inside the 2h grace, a late check-out
    [InlineData(9, 0, true)]   // 09:00 — edge of the grace
    [InlineData(9, 1, false)]  // 09:01 — past grace: now a forgotten check-out, not a live shift
    [InlineData(13, 0, false)] // afternoon — must never linger this far
    public void OvernightWindow_EndsAtShiftEndPlusTwoHours(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, ReportQueryService.WithinOvernightWindow(Night(), new TimeOnly(hour, minute), new DateOnly(2026, 9, 2)));
    }

    [Fact]
    public void Shirin_ADayWorkerWhoForgotToCheckOut_IsExcludedByTheOvernightGuard()
    {
        // Her real shift is a day shift and her check-in was 10:58 the day before. The first guard —
        // IsOvernight — is false, so her open record is never even considered for carry-over,
        // whatever the hour. That is the guard that fixes the reported "İşdə 10:58 at 09:10".
        Assert.False(Day().IsOvernight);
    }
}
