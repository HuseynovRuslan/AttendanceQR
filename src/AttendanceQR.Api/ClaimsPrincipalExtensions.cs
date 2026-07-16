using System.Security.Claims;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Api;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated employee's id, from the JWT "sub" claim.
    ///
    /// Safe to call without a guard inside any [Authorize]d action, which is the only place it makes
    /// sense: the token cannot have been accepted unless "sub" already parsed as a Guid belonging to
    /// an existing, active employee — Program.cs's OnTokenValidated parses it, looks the account up
    /// and fails the token otherwise. Every call site used to re-check that by hand and return 401,
    /// which could not fire.
    ///
    /// Throws rather than returning null because there is no legitimate way to get here without a
    /// valid token. If this ever throws, the auth pipeline is broken — a 500 is the honest answer,
    /// not a 401 blamed on the caller.
    /// </summary>
    public static Guid EmployeeId(this ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("sub"), out var id)
            ? id
            : throw new InvalidOperationException(
                "No usable 'sub' claim. EmployeeId() is only valid inside an authenticated action — " +
                "an [AllowAnonymous] endpoint has no authenticated employee to name.");

    /// <summary>
    /// The authenticated employee's role, from the JWT "role" claim. Same contract as
    /// <see cref="EmployeeId"/>: JwtService writes this claim unconditionally on every token it
    /// issues, so inside an [Authorize]d action it is always present and always parses.
    ///
    /// This is the role as of ISSUANCE. It is trustworthy because changing an employee's role bumps
    /// their TokenVersion (AdminController.Update), which fails every token issued before the change
    /// — otherwise a demoted admin would keep an "Admin" claim forever, since these tokens do not
    /// expire.
    /// </summary>
    public static EmployeeRole Role(this ClaimsPrincipal user)
        => Enum.TryParse<EmployeeRole>(user.FindFirstValue("role"), out var role)
            ? role
            : throw new InvalidOperationException(
                "No usable 'role' claim. Role() is only valid inside an authenticated action.");
}
