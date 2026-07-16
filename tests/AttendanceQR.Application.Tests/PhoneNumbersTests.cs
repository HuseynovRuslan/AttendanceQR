using AttendanceQR.Api;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Covers <see cref="PhoneNumbers.Normalize"/> — the canonical form every phone login and every
/// stored Employee.PhoneNumber goes through. If two spellings of one number stop normalizing to the
/// same string, an employee silently can't log in; if two different numbers start colliding, one
/// employee reaches another's account. Both are why this is pinned.
/// </summary>
public class PhoneNumbersTests
{
    [Theory]
    // Every way an Azerbaijani number gets typed must land on the same 9 digits.
    [InlineData("0501234567")]
    [InlineData("+994 50 123 45 67")]
    [InlineData("994501234567")]
    [InlineData("+994501234567")]
    [InlineData("(050) 123-45-67")]
    [InlineData("050 123 45 67")]
    [InlineData("0 5 0 1 2 3 4 5 6 7")]
    public void All_spellings_of_one_number_normalize_alike(string input)
        => Assert.Equal("501234567", PhoneNumbers.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]         // no digits at all
    [InlineData("123456")]      // 6 digits — below the 7-digit floor
    [InlineData("+994 5")]
    public void Too_short_or_empty_is_null(string? input)
        => Assert.Null(PhoneNumbers.Normalize(input));

    [Fact]
    public void Seven_digits_is_the_lower_bound_and_is_kept_as_is()
    {
        // Exactly at the floor: kept whole, not padded — the ^9.. slice only trims longer input.
        Assert.Equal("1234567", PhoneNumbers.Normalize("1234567"));
        Assert.Null(PhoneNumbers.Normalize("123456"));
    }

    [Fact]
    public void Longer_than_nine_digits_keeps_the_last_nine()
    {
        // An international prefix is dropped, not the subscriber number.
        Assert.Equal("501234567", PhoneNumbers.Normalize("00994501234567"));
    }

    [Fact]
    public void Different_subscribers_do_not_collide()
    {
        Assert.NotEqual(PhoneNumbers.Normalize("0501234567"), PhoneNumbers.Normalize("0551234567"));
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        // Stored values get re-normalized on the way through login; a second pass must not change them.
        var once = PhoneNumbers.Normalize("+994 50 123 45 67");
        Assert.Equal(once, PhoneNumbers.Normalize(once));
    }
}
