using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Who a manager may hand a task to, and what «həll olundu» is for.
///
/// CanFieldCheckIn gates SELF-REPORT — a visit the worker invents, at a place they name, with no
/// fence — which is the only reason that flag needs to exist. It was also being demanded of the
/// person being ASSIGNED to, and the two are different questions: an assigned visit is a manager's
/// decision about their own branch's staff, and the worker can already see it (GET /mine) and check
/// in and out of it without the flag. Answering the wrong question emptied the dropdown — at CleanFix
/// nobody held the flag, so a manager who wanted to hand out «süpür / zibili boşalt / otu biç» opened
/// the form and found no names in it at all.
///
/// These pin the shapes the controller relies on; the endpoint's own scope rule
/// (LocationScopeRules.CanManageEmployeeAsync — their branches, plain staff only) is what bounds it,
/// and is covered by the manager-scope suite.
/// </summary>
public class FieldTaskAssignTests
{
    private static Employee Worker(bool selfReport) => new()
    {
        Id = Guid.NewGuid(), FullName = "İşçi", Role = EmployeeRole.Employee,
        IsActive = true, PasswordHash = "h", CanFieldCheckIn = selfReport,
    };

    [Fact]
    public void A_worker_without_the_self_report_flag_is_still_a_person_you_can_assign_work_to()
    {
        // The whole bug in one line: this used to be the test for "may be assigned", and it is not.
        var w = Worker(selfReport: false);

        Assert.False(w.CanFieldCheckIn);
        Assert.Equal(EmployeeRole.Employee, w.Role);
        Assert.True(w.IsActive);
    }

    [Fact]
    public void An_unreviewed_visit_carries_no_verdict()
    {
        // Null is the honest third state: nobody has looked yet. It is not «həll olunmadı», which is a
        // judgement somebody made and put their name to.
        var v = new FieldVisit { EmployeeId = Guid.NewGuid(), VisitDate = new DateOnly(2026, 9, 5) };

        Assert.Null(v.ReviewOk);
        Assert.Null(v.ReviewedAtUtc);
        Assert.Null(v.ReviewedByEmployeeId);
    }

    [Fact]
    public void The_verdict_is_separate_from_the_status_the_worker_writes()
    {
        // «Tamamlandı» means the worker checked out. It does not mean the yard is clean, and before
        // the verdict existed the board's only two endings were that word and «Ləğv» — one written by
        // the person being judged, the other erasing the visit rather than judging it.
        var v = new FieldVisit
        {
            EmployeeId = Guid.NewGuid(), VisitDate = new DateOnly(2026, 9, 5),
            Status = FieldVisitStatus.Completed,
            ReviewOk = false,
            ReviewedAtUtc = new DateTime(2026, 9, 5, 15, 0, 0, DateTimeKind.Utc),
            ReviewNote = "kanal təmizlənməyib",
        };

        Assert.Equal(FieldVisitStatus.Completed, v.Status);
        Assert.False(v.ReviewOk);
        Assert.Equal("kanal təmizlənməyib", v.ReviewNote);
    }

    [Fact]
    public void A_verdict_can_be_changed_when_the_work_is_put_right()
    {
        // Re-reviewable on purpose: a manager who marked «həll olunmadı», had it fixed and came back
        // must be able to say so, or the board keeps a red mark against a job that was finished.
        var v = new FieldVisit
        {
            EmployeeId = Guid.NewGuid(), VisitDate = new DateOnly(2026, 9, 5),
            Status = FieldVisitStatus.Completed, ReviewOk = false, ReviewNote = "otu biçilməyib",
        };

        v.ReviewOk = true;
        v.ReviewNote = null;

        Assert.True(v.ReviewOk);
        Assert.Null(v.ReviewNote);
    }
}
