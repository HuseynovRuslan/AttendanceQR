using System.Security.Cryptography;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// What counts as an acceptable PIN, in one place — the shape check used to be a `^\d{4}$` regex
/// copied into two controllers, and nothing anywhere refused the guesses an attacker actually tries
/// first.
///
/// A 4-digit PIN is 10,000 combinations. The lockout store bounds how fast someone can walk that
/// space, but it does nothing about the top of the list: `0000`, `1234`, `1212`. Blocking those is
/// the cheapest possible improvement to every account's floor.
///
/// Deliberately NARROW — about 1% of the space:
///   • all four digits the same    (0000, 1111 …)          10
///   • a straight run either way   (0123 … 6789, 9876 …)   14
///   • a repeated pair             (1212, 2020 …)          90
/// It does NOT block birth years (1990, 2005 …). In Azerbaijan those are extremely common choices,
/// and a rule that rejects a third of what people naturally pick teaches them to write the PIN on
/// the poster instead. The lockout and the per-IP cap are what make a guessable-but-not-obvious PIN
/// survivable; this only removes the handful an attacker would try in their first ten attempts.
///
/// Applied when a PIN is SET, never at login — an existing weak PIN keeps working until its owner
/// changes it. Re-validating at login would lock people out of their own accounts overnight, which
/// is a worse outcome than a weak PIN.
/// </summary>
public static class PinRules
{
    public const int Length = 4;

    /// <summary>Exactly four digits. The shape rule, unchanged from the regex it replaces.</summary>
    public static bool IsWellFormed(string? pin) =>
        pin is { Length: Length } && pin.All(char.IsAsciiDigit);

    /// <summary>True for the guesses an attacker starts with. Assumes <see cref="IsWellFormed"/>.</summary>
    public static bool IsTooWeak(string pin)
    {
        if (!IsWellFormed(pin))
            return false; // shape is a separate answer — don't conflate "malformed" with "weak"

        // 1111 — and 1212 / 2020, which is the same idea one step out.
        if (pin[0] == pin[1] && pin[1] == pin[2] && pin[2] == pin[3]) return true;
        if (pin[0] == pin[2] && pin[1] == pin[3]) return true;

        // 1234 and 4321, including the wrap-free runs only (9012 is not an obvious guess).
        var ascending = true;
        var descending = true;
        for (var i = 1; i < Length; i++)
        {
            if (pin[i] != pin[i - 1] + 1) ascending = false;
            if (pin[i] != pin[i - 1] - 1) descending = false;
        }
        return ascending || descending;
    }

    /// <summary>
    /// A cryptographically random PIN that is not one of the weak ones. Used for every temporary PIN
    /// the system hands out: a temp PIN of "1234" is precisely the one an attacker guesses first, and
    /// it stays valid until the employee gets round to changing it.
    /// </summary>
    public static string Generate()
    {
        while (true)
        {
            var pin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
            if (!IsTooWeak(pin))
                return pin;
        }
    }
}
