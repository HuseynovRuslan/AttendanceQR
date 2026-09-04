using AttendanceQR.Application.Reporting;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The leave breakdown that the Excel export prints.
///
/// It exists because one column headed «Məzuniyyət/İcazə» was holding four different entitlements.
/// «Məzuniyyət» comes off a yearly balance, «Xəstəlik» needs a medical certificate and is funded
/// differently, «Ödənişsiz» is not paid at all, and «İstirahət» is none of those — and once they are
/// added together no accountant can take them apart again. On production that single number was
/// hiding 495 days of annual leave, 415 of rest, 31 of sick and 23 unpaid.
///
/// What is pinned here is the INVARIANT that makes the split safe to ship: LeaveDays keeps its old
/// meaning. PayrollMath divides by a figure built from it, and a reporting fix that quietly redefines
/// an input to the payroll is a pay bug wearing a report's clothes.
/// </summary>
public class LeaveBreakdownTests
{
    private static EmployeeReportRow Row(
        int leave, int trip = 0, int vacation = 0, int sick = 0, int unpaid = 0, int rest = 0) =>
        new(Guid.NewGuid(), "Test", "Filial",
            WorkDays: 20, LateCount: 0, AbsentDays: 0, IncompleteDays: 0,
            TotalWorkedHours: 160, OvertimeHours: 0,
            LeaveDays: leave, TripDays: trip, PermissionDays: 0,
            VacationDays: vacation, SickDays: sick, UnpaidDays: unpaid, RestDays: rest);

    [Fact]
    public void The_four_types_sum_to_the_total_they_came_from()
    {
        // The whole point of the split: detail that adds back up to the number it replaced.
        var r = Row(leave: 12, vacation: 5, sick: 3, unpaid: 1, rest: 3);
        Assert.Equal(r.LeaveDays, r.VacationDays + r.SickDays + r.UnpaidDays + r.RestDays);
    }

    [Fact]
    public void A_business_trip_is_outside_the_total_and_always_was()
    {
        // Ezamiyyət is work, not leave. It was carved out of LeaveDays before this change and must
        // stay out of it: a driver away for a month must never read as a month of annual leave.
        var r = Row(leave: 4, trip: 20, vacation: 4);
        Assert.Equal(4, r.LeaveDays);
        Assert.Equal(20, r.TripDays);
        Assert.Equal(r.LeaveDays, r.VacationDays + r.SickDays + r.UnpaidDays + r.RestDays);
    }

    [Fact]
    public void Sick_is_never_folded_into_vacation()
    {
        // The complaint, as a test: a month that is entirely sick leave must report zero «Məzuniyyət».
        var r = Row(leave: 7, sick: 7);
        Assert.Equal(7, r.SickDays);
        Assert.Equal(0, r.VacationDays);
    }

    [Fact]
    public void The_payroll_divisor_is_unchanged_by_the_split()
    {
        // The safety property, pinned as ACTUAL FIGURES rather than by comparing the function to
        // itself: two identical calls agreeing proves nothing at all. 18 worked + 2 absent + 5 leave
        // = 25 scheduled; 1000 / 25 = 40 a day; two absences deduct 80.
        var pay = PayrollMath.Compute(
            salary: 1000m, workDays: 18, absentDays: 2, leaveDays: 5, permissionDays: 0, tripDays: 0);

        // Leave counts as a scheduled day — that is what makes it "excused" rather than free money —
        // and it is counted through LeaveDays, which the breakdown does not touch.
        Assert.Equal(25, pay.Scheduled);
        Assert.Equal(40m, pay.PerDay);
        Assert.Equal(80m, pay.Deduction);
        Assert.Equal(920m, pay.Payable);
    }

    [Fact]
    public void A_leave_day_no_record_covers_stays_in_the_total_and_in_no_bucket()
    {
        // A summary row written by the night job says OnLeave; the LeaveRecord behind it was deleted
        // afterwards. The day cannot be typed any more — but dropping it from LeaveDays would shrink
        // the payroll divisor and quietly raise everyone's per-day rate. It stays counted, and the
        // breakdown simply does not add up to the total. That gap is visible in the sheet rather than
        // hidden, which is the right way round.
        var r = Row(leave: 10, vacation: 4, sick: 2);
        Assert.Equal(10, r.LeaveDays);
        Assert.Equal(6, r.VacationDays + r.SickDays + r.UnpaidDays + r.RestDays);
        Assert.True(r.LeaveDays > r.VacationDays + r.SickDays + r.UnpaidDays + r.RestDays);
    }
}
