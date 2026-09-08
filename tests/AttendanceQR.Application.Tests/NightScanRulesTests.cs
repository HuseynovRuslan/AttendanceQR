using AttendanceQR.Application.Common;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The night shift's two-date day, and the scans it makes ambiguous.
///
/// Written from three people's real mornings on 2026-09-08. Hacıyeva Güllü (21:00–07:00) closed her
/// night at 06:05 and scanned once more at 09:35; Aleksey Belyayev (18:00–08:00) at 10:28; Əliyev
/// Bəhrəm at 08:00. Each of those extra scans opened a day that would have swallowed that night's
/// work — the evening arrival read as the stray's check-out, twelve hours of "work" spent asleep at
/// home, and the shift actually worked never opened at all.
/// </summary>
public class NightScanRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);
    private static readonly DateOnly Yesterday = new(2026, 9, 7);

    // ── the morning half ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_second_morning_scan_after_the_night_was_closed_is_a_repeat()
    {
        // Güllü: out at 06:05, scanned again at 09:35. Her exit is already on the books.
        Assert.True(NightScanRules.IsRepeatOfThisMorningsExit(
            new TimeOnly(9, 35), Today, todayShiftIsOvernight: true, nightClosedOnLocalDate: Today));
    }

    [Fact]
    public void A_morning_scan_whose_night_was_never_closed_is_left_alone()
    {
        // This is the condition that makes refusing safe. Nothing of theirs is recorded for that
        // night, so the scan may well BE the check-out — and it must be allowed to do its work.
        Assert.False(NightScanRules.IsRepeatOfThisMorningsExit(
            new TimeOnly(8, 0), Today, todayShiftIsOvernight: true, nightClosedOnLocalDate: null));

        // Closed, but yesterday — a different night, not this morning's. Əliyev Bəhrəm's shape.
        Assert.False(NightScanRules.IsRepeatOfThisMorningsExit(
            new TimeOnly(8, 0), Today, todayShiftIsOvernight: true, nightClosedOnLocalDate: Yesterday));
    }

    [Fact]
    public void A_day_worker_is_never_touched_by_this()
    {
        // The whole rule exists because one working day spans two dates. A day shift's does not.
        Assert.False(NightScanRules.IsRepeatOfThisMorningsExit(
            new TimeOnly(9, 35), Today, todayShiftIsOvernight: false, nightClosedOnLocalDate: Today));
    }

    [Fact]
    public void An_afternoon_scan_is_not_a_morning_scan()
    {
        Assert.False(NightScanRules.IsRepeatOfThisMorningsExit(
            new TimeOnly(14, 0), Today, todayShiftIsOvernight: true, nightClosedOnLocalDate: Today));
    }

    // ── the evening half ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_evening_arrival_wins_over_a_stray_opened_that_morning()
    {
        // Güllü at 21:30 with 09:35 sitting open: this is her arrival, not the stray's departure.
        Assert.True(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(21, 30), new TimeOnly(21, 0), isOvernight: true, openCheckInLocal: new TimeOnly(9, 35)));

        // Aleksey: 18:00–08:00, stray at 10:28, arrives 18:05.
        Assert.True(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(18, 5), new TimeOnly(18, 0), isOvernight: true, openCheckInLocal: new TimeOnly(10, 28)));
    }

    [Fact]
    public void An_early_arrival_and_its_own_check_out_are_left_alone()
    {
        // THE case this must not break: they came at 20:30 for a 21:00 start — an ordinary check-in —
        // and scan again later. That second scan is a check-out and must stay one. 20:30 is not a
        // morning scan, so it never reaches the rule.
        Assert.False(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(23, 0), new TimeOnly(21, 0), isOvernight: true, openCheckInLocal: new TimeOnly(20, 30)));
    }

    [Fact]
    public void Before_the_shift_starts_nothing_is_rewritten()
    {
        // A stray is only rescued once the night has actually begun; at four in the afternoon the
        // person has not arrived for anything yet.
        Assert.False(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(16, 0), new TimeOnly(21, 0), isOvernight: true, openCheckInLocal: new TimeOnly(9, 35)));
    }

    [Fact]
    public void A_day_shift_is_never_rewritten()
    {
        // 09:00–18:00 with a 09:05 check-in and an 18:00 scan: an ordinary day, ordinary check-out.
        Assert.False(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(18, 0), new TimeOnly(9, 0), isOvernight: false, openCheckInLocal: new TimeOnly(9, 5)));
    }

    [Fact]
    public void A_shift_that_starts_in_the_morning_is_out_of_scope()
    {
        // Guard against an absurd rota (06:00 → 05:00) turning an ordinary morning check-in into a
        // rewritten arrival. Only shifts that start from midday onward are nights in this sense.
        Assert.False(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(7, 0), new TimeOnly(6, 0), isOvernight: true, openCheckInLocal: new TimeOnly(6, 30)));
    }

    [Fact]
    public void A_day_with_no_open_check_in_is_not_a_stray()
    {
        Assert.False(NightScanRules.IsEveningArrivalOverStrayDay(
            new TimeOnly(21, 30), new TimeOnly(21, 0), isOvernight: true, openCheckInLocal: null));
    }
}
