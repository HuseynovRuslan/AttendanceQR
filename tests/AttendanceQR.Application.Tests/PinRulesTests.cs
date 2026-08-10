using AttendanceQR.Infrastructure.Security;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The floor under every account's credential. A 4-digit PIN is 10,000 combinations; the lockout
/// bounds how fast someone walks that space, but nothing used to refuse the guesses an attacker
/// starts with. These pin both halves of the deal: the obvious ones are refused, and the ones people
/// actually pick — birth years above all — are NOT, because a rule that rejects a third of natural
/// choices only teaches people to write the PIN on the wall.
/// </summary>
public class PinRulesTests
{
    [Theory]
    [InlineData("0000")]
    [InlineData("1111")]
    [InlineData("9999")]
    [InlineData("1234")]
    [InlineData("2345")]
    [InlineData("6789")]
    [InlineData("4321")]
    [InlineData("9876")]
    [InlineData("1212")]
    [InlineData("2020")]
    [InlineData("1010")]
    public void The_obvious_guesses_are_refused(string pin)
    {
        Assert.True(PinRules.IsWellFormed(pin));   // shape is fine — it is the CHOICE that is bad
        Assert.True(PinRules.IsTooWeak(pin));
    }

    [Theory]
    [InlineData("1990")]  // a birth year — extremely common here, and deliberately allowed
    [InlineData("2005")]
    [InlineData("1997")]
    [InlineData("7497")]
    [InlineData("4830")]
    [InlineData("9012")]  // wraps past 9, so not the straight run an attacker types
    [InlineData("1123")]
    public void Ordinary_choices_are_allowed(string pin)
    {
        Assert.True(PinRules.IsWellFormed(pin));
        Assert.False(PinRules.IsTooWeak(pin));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" 123")]
    public void Anything_that_is_not_four_digits_is_malformed(string? pin)
        => Assert.False(PinRules.IsWellFormed(pin));

    [Fact]
    public void Malformed_is_not_reported_as_weak()
    {
        // The two answers stay separate so a caller can say "PinInvalid" and "PinTooWeak" honestly —
        // telling someone their 3-digit entry is "too weak" would send them looking for a better PIN
        // instead of a fourth digit.
        Assert.False(PinRules.IsTooWeak("123"));
        Assert.False(PinRules.IsTooWeak("abcd"));
    }

    [Fact]
    public void The_blocklist_stays_narrow()
    {
        // ~1% of the space. If a future edit makes this materially bigger, it has started rejecting
        // choices real people make, and this test is the tripwire.
        var weak = Enumerable.Range(0, 10_000).Count(n => PinRules.IsTooWeak(n.ToString("D4")));
        Assert.InRange(weak, 100, 130);
    }

    [Fact]
    public void Generated_temporary_pins_are_never_weak()
    {
        // Every temp PIN the system hands out goes through this. A temp "1234" is exactly the guess
        // an attacker tries first, and it stays valid until the employee gets round to changing it.
        for (var i = 0; i < 500; i++)
        {
            var pin = PinRules.Generate();
            Assert.True(PinRules.IsWellFormed(pin));
            Assert.False(PinRules.IsTooWeak(pin));
        }
    }
}
