using AttendanceQR.Application.Reporting;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The rule that stopped an onboarding from being billed as absenteeism.
///
/// Bulk import stamps ActivatedAtUtc = now, so two hundred people "started" the instant a spreadsheet
/// was pasted — while the phones were still in a drawer. At Bakı Abadlıq Xidməti that produced 876
/// absent-days across 207 people in seven days, half of every scheduled day in the company, and each
/// one deducts a day's pay. This is the guard, and both halves of it matter: the window stops the
/// forgiveness becoming permanent, and the first-scan test stops it applying to somebody who is
/// demonstrably up and running.
/// </summary>
public class OnboardingGraceTests
{
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    /// 2026-08-25 10:00 Baku = 06:00 UTC — the real import moment, near enough.
    private static readonly DateTime Imported = new(2026, 8, 25, 6, 0, 0, DateTimeKind.Utc);

    private static bool Onboarding(string date, DateTime? activated, string? firstAttendance) =>
        AttendanceCalculator.IsStillOnboarding(
            DateOnly.Parse(date), activated,
            firstAttendance is null ? null : DateOnly.Parse(firstAttendance), Baku);

    [Fact]
    public void The_days_between_the_import_and_the_first_scan_are_not_absences()
    {
        // Imported on the 25th, phone finally working on the 28th. The 26th and 27th were an
        // onboarding, not two days of not turning up.
        Assert.True(Onboarding("2026-08-26", Imported, "2026-08-28"));
        Assert.True(Onboarding("2026-08-27", Imported, "2026-08-28"));
    }

    [Fact]
    public void The_forgiveness_ends_the_moment_they_first_scan()
    {
        // From the first scan onward they have a working phone, so a missing day is a real absence
        // again — including the day of the scan itself.
        Assert.False(Onboarding("2026-08-28", Imported, "2026-08-28"));
        Assert.False(Onboarding("2026-08-29", Imported, "2026-08-28"));
        Assert.False(Onboarding("2026-09-01", Imported, "2026-08-28"));
    }

    [Fact]
    public void Someone_who_has_never_scanned_is_never_judged_at_all()
    {
        // Changed 2026-09-08, and the register is why. Two hundred and twenty-seven active people had
        // never scanned once; the ten whose window had expired were carrying 28–45 Qayıb days each,
        // and payroll deducts a day per Qayıb. Nobody had decided any of it — the absence of evidence
        // was being read as evidence, against people this system cannot show were ever handed a
        // working way to scan.
        Assert.True(Onboarding("2026-09-02", Imported, null));
        Assert.True(Onboarding("2026-09-09", Imported, null));   // day 15 — the window no longer closes
        Assert.True(Onboarding("2027-03-01", Imported, null));   // nor ever

        // What replaces it is a person: AbsenceMark. A manager who watched somebody not turn up says
        // so, and their name goes on the day.
    }

    [Fact]
    public void The_window_boundary_is_inclusive_on_the_last_day()
    {
        // The window now governs only the gap BEFORE a first scan — somebody who never scans is never
        // judged at all. So this is measured against a person who did eventually start, on 01.10:
        // activated 25.08 + 14 days = 08.09 is the last forgiven day, and the 9th is judged.
        Assert.True(Onboarding("2026-09-08", Imported, "2026-10-01"));
        Assert.False(Onboarding("2026-09-09", Imported, "2026-10-01"));
    }

    [Fact]
    public void A_late_first_scan_still_ends_the_forgiveness_early()
    {
        // Scanned on day 3, then vanished. Days 4-14 are absences, not onboarding — the window is a
        // ceiling on the excuse, never a floor under it.
        Assert.False(Onboarding("2026-09-01", Imported, "2026-08-27"));
    }

    [Fact]
    public void An_account_with_no_activation_date_is_never_onboarding()
    {
        // Invited but never activated: a different guard already skips them entirely, and this one
        // must not quietly claim them.
        Assert.False(Onboarding("2026-08-26", null, null));
    }

    [Fact]
    public void A_long_standing_employee_is_untouched()
    {
        // Activated in June and working since the 2nd: nothing here applies to them at all.
        var june = new DateTime(2026, 6, 1, 6, 0, 0, DateTimeKind.Utc);
        Assert.False(Onboarding("2026-08-26", june, "2026-06-02"));

        // Activated in June and never once scanned is a different person, and not a long-standing
        // employee at all — it is an account nobody ever set up. It is not judged; a manager marks
        // the day if somebody was actually expected and did not come.
        Assert.True(Onboarding("2026-08-26", june, null));
    }

    [Fact]
    public void The_activation_instant_is_read_in_company_time()
    {
        // 2026-08-25 21:00 UTC is already the 26th in Baku (UTC+4). Read as UTC the window would end
        // a day early, and the last day of somebody's setup would be billed to them. Measured against
        // somebody who did eventually start, since that is the only case the window still governs.
        var lateUtc = new DateTime(2026, 8, 25, 21, 0, 0, DateTimeKind.Utc);
        Assert.True(Onboarding("2026-09-09", lateUtc, "2026-10-01"));    // 26.08 + 14 = 09.09
        Assert.False(Onboarding("2026-09-10", lateUtc, "2026-10-01"));
    }
}
