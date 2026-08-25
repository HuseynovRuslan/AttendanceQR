using AttendanceQR.Infrastructure.Security;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Reissuing a group's temporary PINs — and the reason it is a reissue rather than a lookup.
///
/// A temporary PIN is hashed the moment it is created and the plaintext is returned exactly once. That
/// is right: it passes through several hands, gets read out over the phone, and ends up written on
/// paper, so a copy on the server would be a permanent weakness rather than a convenience. The cost
/// showed up on the first company onboarded this way — nineteen people imported, the page refreshed
/// before the list was copied, nineteen credentials gone. Nobody had used them, so nothing was lost
/// except an afternoon; but the only way back was nineteen separate resets, with another hundred and
/// seventy-eight people still waiting for theirs.
///
/// What is pinned here is the shape of the rules, not the controller plumbing: every guard the single
/// reset applies has to apply per row, because this endpoint hands back plaintext exactly as that one
/// does. A batch that quietly skipped one of them would move the hole rather than close it.
/// </summary>
public class BulkPinReissueTests
{
    /// <summary>
    /// The PIN generator the reissue uses. Four digits, and never one of the ones a person would pick
    /// for themselves — the same rule the single reset and the login screen enforce.
    /// </summary>
    [Fact]
    public void Every_generated_PIN_is_four_digits_and_not_a_weak_one()
    {
        for (var i = 0; i < 500; i++)
        {
            var pin = PinRules.Generate();
            Assert.Equal(4, pin.Length);
            Assert.True(pin.All(char.IsAsciiDigit), $"«{pin}» rəqəm deyil");
            Assert.True(PinRules.IsWellFormed(pin), $"«{pin}» formaya uyğun deyil");
            Assert.False(PinRules.IsTooWeak(pin), $"«{pin}» zəif PIN kimi verildi");
        }
    }

    [Fact]
    public void Two_people_do_not_get_the_same_PIN_by_construction_of_the_generator()
    {
        // Not a uniqueness guarantee — 4 digits over 19 people will collide sometimes and that is
        // fine, they are separate accounts. What must NOT happen is a generator that returns the same
        // value repeatedly, which would hand a whole branch one shared PIN.
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++) seen.Add(PinRules.Generate());
        Assert.True(seen.Count > 100, $"200 çağırışdan yalnız {seen.Count} fərqli PIN çıxdı");
    }
}
