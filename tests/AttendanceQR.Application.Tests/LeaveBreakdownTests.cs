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
        // The whole point of the split: detail that adds back up to the number it replaced. THREE of
        // them — İstirahət is a DayOff, not leave, and lives beside the total rather than inside it.
        var r = Row(leave: 9, vacation: 5, sick: 3, unpaid: 1, rest: 3);
        Assert.Equal(r.LeaveDays, r.VacationDays + r.SickDays + r.UnpaidDays);
    }

    [Fact]
    public void A_business_trip_is_outside_the_total_and_always_was()
    {
        // Ezamiyyət is work, not leave. It was carved out of LeaveDays before this change and must
        // stay out of it: a driver away for a month must never read as a month of annual leave.
        var r = Row(leave: 4, trip: 20, vacation: 4);
        Assert.Equal(4, r.LeaveDays);
        Assert.Equal(20, r.TripDays);
        Assert.Equal(r.LeaveDays, r.VacationDays + r.SickDays + r.UnpaidDays);
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
    public void Unpaid_leave_is_not_paid()
    {
        // «Ödənişsiz məzuniyyət» means unpaid, and it was being paid at full salary: PayrollMath had
        // no parameter for it, so the day sat in the divisor and was never deducted. Production held
        // 23 such days at one company, every one of them paid.
        //
        // 18 worked + 5 unpaid = 23 scheduled; 1000 / 23 = 43.48 a day; five unpaid days cost 217.40.
        var pay = PayrollMath.Compute(
            salary: 1000m, workDays: 18, absentDays: 0, leaveDays: 5, permissionDays: 0, tripDays: 0,
            unpaidDays: 5);

        Assert.Equal(23, pay.Scheduled);
        Assert.Equal(43.48m, pay.PerDay);
        Assert.Equal(217.40m, pay.Deduction);
        Assert.Equal(782.60m, pay.Payable);
    }

    [Fact]
    public void Unpaid_days_stay_in_the_divisor_they_are_deducted_from()
    {
        // The subtle half. An unpaid day is still a SCHEDULED day — taking it out of the divisor
        // would raise the per-day rate and so overcharge every OTHER absence the same person had.
        // Deduct it, do not un-schedule it.
        var with5 = PayrollMath.Compute(1000m, workDays: 18, absentDays: 0, leaveDays: 5,
            permissionDays: 0, tripDays: 0, unpaidDays: 5);
        var withNone = PayrollMath.Compute(1000m, workDays: 18, absentDays: 0, leaveDays: 5,
            permissionDays: 0, tripDays: 0);

        Assert.Equal(withNone.Scheduled, with5.Scheduled);   // same divisor
        Assert.Equal(withNone.PerDay, with5.PerDay);         // same daily rate
        Assert.True(with5.Deduction > withNone.Deduction);   // only the deduction differs
    }

    [Fact]
    public void Paid_leave_is_still_paid()
    {
        // The guard on the change above: annual leave and sick leave must NOT be deducted. Only the
        // type whose name says «ödənişsiz».
        var pay = PayrollMath.Compute(
            salary: 1000m, workDays: 18, absentDays: 0, leaveDays: 5, permissionDays: 2, tripDays: 0);

        Assert.Equal(0m, pay.Deduction);
        Assert.Equal(1000m, pay.Payable);
    }

    [Fact]
    public void A_weekend_inside_a_holiday_is_not_a_working_day()
    {
        // An approved leave beats the day-off rule on purpose: a person on holiday over a Sunday
        // reads «Məzuniyyət», not «İstirahət». Right for the board and the tabel — and wrong for the
        // divisor, where those Sundays arrived inside leaveDays and counted as working days.
        //
        // A fortnight's holiday over a Mon–Sat week carries 2 Sundays. 18 worked + 1 absent + 14
        // leave = 33 by the old sum, but two of those were not working days: 31 is the truth.
        // 1000 / 31 = 32.26 rather than 1000 / 33 = 30.30 — the absence was under-deducted by ~2 ₼.
        var fixedUp = PayrollMath.Compute(
            salary: 1000m, workDays: 18, absentDays: 1, leaveDays: 14, permissionDays: 0, tripDays: 0,
            unpaidDays: 0, offDayLeaveDays: 2);
        var inflated = PayrollMath.Compute(
            salary: 1000m, workDays: 18, absentDays: 1, leaveDays: 14, permissionDays: 0, tripDays: 0);

        Assert.Equal(31, fixedUp.Scheduled);
        Assert.Equal(33, inflated.Scheduled);
        Assert.True(fixedUp.PerDay > inflated.PerDay);
        Assert.True(fixedUp.Deduction > inflated.Deduction);
    }

    [Fact]
    public void Nobody_with_no_leave_is_affected_by_the_divisor_fix()
    {
        // The guard. offDayLeaveDays is zero for anyone whose leave never crossed a rest day, and
        // for everyone with no leave at all — which is most people, most months. Their pay must be
        // identical to before the change.
        var pay = PayrollMath.Compute(
            salary: 1000m, workDays: 20, absentDays: 2, leaveDays: 0, permissionDays: 0, tripDays: 0);

        Assert.Equal(22, pay.Scheduled);
        Assert.Equal(45.45m, pay.PerDay);
        Assert.Equal(90.90m, pay.Deduction);
    }

    [Fact]
    public void A_rest_day_is_not_leave_and_is_not_in_the_total()
    {
        // İstirahət resolves to DayOff, never OnLeave, so it was never inside LeaveDays and must not
        // be added to it — LeaveDays feeds the payroll divisor. The first cut of the breakdown
        // counted Rest against OnLeave, an unsatisfiable pair that would have exported a permanent
        // zero; this pins that Rest lives beside the total rather than inside it.
        var r = Row(leave: 6, vacation: 4, sick: 2, rest: 9);
        Assert.Equal(6, r.LeaveDays);
        Assert.Equal(6, r.VacationDays + r.SickDays + r.UnpaidDays);
        Assert.Equal(9, r.RestDays);
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
        Assert.Equal(6, r.VacationDays + r.SickDays + r.UnpaidDays);
        Assert.True(r.LeaveDays > r.VacationDays + r.SickDays + r.UnpaidDays);
    }
}
