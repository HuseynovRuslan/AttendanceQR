using AttendanceQR.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A scan soon after arriving, driven through the real scan endpoint.
///
/// Reported from the field, and it cost whole days of pay: with no signal, people who were not sure
/// their morning tap had worked tapped again. Replayed later, the second tap became a CHECK-OUT — the
/// day that began at 07:38 closed at 07:44 — and the real exit that evening was refused as «artıq
/// tamamlamısınız». Fərəcov Elşən's 07:31 retry sat on his phone all day, was sent at 17:54 when he
/// opened the app to leave, closed his day at 07:31, and then refused the very scan that sent it.
///
/// The rule: within two hours of arriving, a check-out needs the employee's «bəli, çıxıram».
/// </summary>
public class EarlyCheckOutScanTests
{
    /// <summary>
    /// The incident's own minute: 16.09, 17:54 in Baku — when Fərəcov Elşən opened the app to leave.
    ///
    /// Pinned, because these tests read the wall clock through the endpoint and two of them only held
    /// after 09:00 UTC. The offline branch of <see cref="AttendanceController.Scan"/> opens the day on
    /// the UTC date of the time the PHONE sent, not the server's. With the arrival pushed nine hours
    /// back, that timestamp crossed UTC midnight whenever the suite ran before nine in the morning:
    /// the day was then looked up under yesterday's date, no open record was found, and the replayed
    /// scan opened a FRESH check-in instead of being weighed as a check-out — so the first test got
    /// null where it expected «ConfirmEarlyCheckOut», and the second blew up in SingleAsync on the
    /// second row. Every CI run before 09:00 UTC was red for this reason and nothing else.
    /// </summary>
    private static readonly DateTime Anchor = new(2026, 9, 16, 13, 54, 0, DateTimeKind.Utc);

    private static string? Error(IActionResult r) =>
        r is ObjectResult { Value: { } v } ? v.GetType().GetProperty("error")?.GetValue(v) as string : null;

    /// <summary>Check in, then move the arrival <paramref name="minutesAgo"/> into the past.</summary>
    private static async Task<SplitShiftScanTests.Harness> ArrivedAsync(int minutesAgo)
    {
        var h = new SplitShiftScanTests.Harness(secondWindow: false, nowUtc: Anchor);
        Assert.Null(Error(await h.Controller.Scan(h.Scan())));
        var open = await h.Db.AttendanceRecords.SingleAsync();
        open.CheckInAtUtc = Anchor.AddMinutes(-minutesAgo);
        await h.Db.SaveChangesAsync();
        return h;
    }

    [Fact]
    public async Task A_scan_forty_minutes_after_arriving_asks_first_and_changes_nothing()
    {
        using var h = await ArrivedAsync(40);

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("ConfirmEarlyCheckOut", Error(result));
        Assert.Null((await h.Db.AttendanceRecords.SingleAsync()).CheckOutAtUtc);   // still at work
    }

    [Fact]
    public async Task Saying_yes_records_the_early_exit()
    {
        // Somebody who really is going home after forty minutes is recorded exactly as before.
        using var h = await ArrivedAsync(40);

        var result = await h.Controller.Scan(h.Scan() with { ConfirmEarlyCheckOut = true });

        Assert.Null(Error(result));
        Assert.NotNull((await h.Db.AttendanceRecords.SingleAsync()).CheckOutAtUtc);
    }

    [Fact]
    public async Task A_full_day_later_no_question_is_asked()
    {
        using var h = await ArrivedAsync(EarlyCheckOutRules.ConfirmWithinMinutes + 1);

        Assert.Null(Error(await h.Controller.Scan(h.Scan())));
        Assert.NotNull((await h.Db.AttendanceRecords.SingleAsync()).CheckOutAtUtc);
    }

    [Fact]
    public async Task A_morning_retry_replayed_in_the_evening_leaves_the_day_open_for_the_real_exit()
    {
        // Fərəcov Elşən, 16.09: in 07:08, a nervous retry at 07:31 with no signal, sent at 17:54.
        using var h = await ArrivedAsync(9 * 60);
        var checkIn = (await h.Db.AttendanceRecords.SingleAsync()).CheckInAtUtc!.Value;

        var retry = await h.Controller.Scan(h.Scan() with
        {
            Offline = true,
            ClientScanId = Guid.NewGuid(),
            ClientTimestampUtc = checkIn.AddMinutes(23),
        });

        Assert.Equal("ConfirmEarlyCheckOut", Error(retry));
        Assert.Null((await h.Db.AttendanceRecords.SingleAsync()).CheckOutAtUtc);

        // And the scan he actually made on the way out is the one that closes the day.
        var exit = await h.Controller.Scan(h.Scan());

        Assert.Null(Error(exit));
        var day = await h.Db.AttendanceRecords.SingleAsync();
        Assert.NotNull(day.CheckOutAtUtc);
        Assert.True(day.CheckOutAtUtc!.Value - checkIn > TimeSpan.FromHours(8));
    }

    [Fact]
    public async Task An_offline_exit_the_employee_confirmed_on_the_phone_is_recorded()
    {
        // The phone asks before it queues; a «bəli» travels with the queued scan.
        using var h = await ArrivedAsync(9 * 60);
        var checkIn = (await h.Db.AttendanceRecords.SingleAsync()).CheckInAtUtc!.Value;

        var result = await h.Controller.Scan(h.Scan() with
        {
            Offline = true,
            ClientScanId = Guid.NewGuid(),
            ClientTimestampUtc = checkIn.AddMinutes(50),
            ConfirmEarlyCheckOut = true,
        });

        Assert.Null(Error(result));
        Assert.Equal(checkIn.AddMinutes(50), (await h.Db.AttendanceRecords.SingleAsync()).CheckOutAtUtc);
    }

    [Fact]
    public async Task The_five_minute_double_tap_is_still_its_own_answer()
    {
        // Unchanged: a tap seconds after arriving is TooSoonToCheckOut, which the phone already explains.
        using var h = await ArrivedAsync(1);

        Assert.Equal("TooSoonToCheckOut", Error(await h.Controller.Scan(h.Scan() with { ConfirmEarlyCheckOut = true })));
    }

    [Theory]
    [InlineData(10, false, true)]
    [InlineData(119, false, true)]
    [InlineData(120, false, false)]
    [InlineData(10, true, false)]
    public void The_rule_on_its_own(int minutesAfter, bool confirmed, bool asks)
    {
        var inAt = new DateTime(2026, 9, 16, 3, 8, 0, DateTimeKind.Utc);
        Assert.Equal(asks, EarlyCheckOutRules.NeedsConfirmation(inAt, inAt.AddMinutes(minutesAfter), confirmed));
    }

    [Fact]
    public void No_check_in_asks_nothing()
    {
        Assert.False(EarlyCheckOutRules.NeedsConfirmation(null, DateTime.UtcNow, confirmed: false));
    }
}
