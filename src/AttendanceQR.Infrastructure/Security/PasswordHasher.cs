using Microsoft.AspNetCore.Identity;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// Wraps ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMAC-SHA512
/// with a per-hash random salt and a versioned, self-describing output format).
/// <para>
/// Chosen over a third-party library because it is first-party and battle-tested, needs no
/// extra dependency beyond Microsoft.Extensions.Identity.Core, embeds its own salt and
/// iteration count (so hashes stay verifiable across future tuning), and drops straight in
/// if we later adopt full ASP.NET Core Identity. The <c>TUser</c> generic is unused by the
/// default algorithm, so a placeholder object is fine.
/// </para>
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<object> Inner = new();
    private static readonly object Placeholder = new();

    public string Hash(string password) => Inner.HashPassword(Placeholder, password);

    public bool Verify(string hash, string password) =>
        Inner.VerifyHashedPassword(Placeholder, hash, password) != PasswordVerificationResult.Failed;
}
