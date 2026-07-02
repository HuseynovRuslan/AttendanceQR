using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// Opaque single-use activation secret. Not a JWT.
/// <para>
/// Format: <c>{employeeId:N}.{base64url(32 random bytes)}</c>. The employee id is public (it is
/// only <see cref="Domain.Entities.Employee.Id"/>) and lets activation look the account up by a
/// key that survives activation; the random part is the actual secret. Only the SHA256 hash of
/// the <b>random part</b> is persisted (<see cref="Domain.Entities.Employee.InvitationTokenHash"/>)
/// and it is nulled once the account is activated, so the token stays single-use.
/// </para>
/// </summary>
public static class ActivationToken
{
    /// <summary>
    /// Creates a token bound to <paramref name="employeeId"/>. Returns the plaintext token
    /// (shown once at invite time, never stored) and the hash of its random part to persist.
    /// </summary>
    public static (string Token, string RandomHash) Create(Guid employeeId)
    {
        var randomPart = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var token = $"{employeeId:N}.{randomPart}";
        return (token, Hash(randomPart));
    }

    /// <summary>
    /// Splits a token into its employee id and random part. Returns <c>false</c> when the token
    /// is malformed (missing separator, unparseable id, or empty random part).
    /// </summary>
    public static bool TryParse(string? token, out Guid employeeId, out string randomPart)
    {
        employeeId = Guid.Empty;
        randomPart = string.Empty;
        if (string.IsNullOrEmpty(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 2 || parts[1].Length == 0)
            return false;
        if (!Guid.TryParseExact(parts[0], "N", out employeeId))
            return false;

        randomPart = parts[1];
        return true;
    }

    /// <summary>
    /// Constant-time, null-safe check that <paramref name="randomPart"/> matches the stored hash.
    /// Returns <c>false</c> when nothing is stored (e.g. the account is already activated).
    /// </summary>
    public static bool VerifyRandomPart(string randomPart, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
            return false;

        byte[] stored;
        try
        {
            stored = Convert.FromBase64String(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var computed = SHA256.HashData(Encoding.UTF8.GetBytes(randomPart));
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }

    /// <summary>SHA256 hash (Base64) of a value — the form stored/compared in the database.</summary>
    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
