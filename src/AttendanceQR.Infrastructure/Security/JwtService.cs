using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AttendanceQR.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AttendanceQR.Infrastructure.Security;

/// <summary>
/// Issues the <b>login</b> JWT. This is entirely separate from the activation token: this
/// token is a signed, self-describing bearer credential proving an authenticated session,
/// whereas the activation token is an opaque one-time secret used only to claim an account.
/// </summary>
public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _options;

    public JwtService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(Employee employee)
        => Write(BaseClaims(employee), DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes));

    public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes,
        bool readOnly = false)
    {
        var claims = BaseClaims(employee);
        // Marks this session as a support impersonation and by whom. Advisory — for the log and a client
        // banner. The security boundary is still tid + tv: the token is confined to exactly this
        // employee in this tenant, so it can never reach another company or escalate.
        claims.Add(new Claim("imp", impersonatedBy.ToString()));
        // ...and never the "mcp" flag, even when the borrowed admin is still on the temporary PIN —
        // which on the day a company is created they always are. The claim is a client instruction:
        // AdminRoute/ProtectedRoute redirect to the forced "set your PIN" screen while it is set, and
        // that screen's only button refuses an impersonation session (AuthController.SetInitialPin).
        // Leaving it on would send the operator to a dead end on exactly the tenant this exists to set
        // up. Nothing is weakened: the customer's forced change lives on Employee.MustChangePin in the
        // database — which is what the server-side gate reads — so their own next login still faces it.
        claims.RemoveAll(c => c.Type == "mcp");
        // Read-only session: ViewOnlyBoundary refuses every mutating request carrying this. Unlike the
        // "imp" claim above, this one IS a security boundary, not advisory — it is the only thing
        // standing between a viewer and 113 write endpoints.
        if (readOnly) claims.Add(new Claim("ro", "1"));
        return Write(claims, DateTime.UtcNow.AddMinutes(expiryMinutes));
    }

    private static List<Claim> BaseClaims(Employee employee)
    {
        var claims = new List<Claim>
        {
            new("sub", employee.Id.ToString()),
            // Phone-only employees have no email (it became nullable). A Claim value must never be null
            // or the whole login 500s, so fall back to empty — the email claim is informational only.
            new("email", employee.Email ?? string.Empty),
            new("role", employee.Role.ToString()),
            // Checked against Employee.TokenVersion on every request (Program.cs
            // OnTokenValidated) — lets change-password invalidate every other outstanding token.
            new("tv", employee.TokenVersion.ToString()),
            // Multi-tenancy: which company this session belongs to. OnTokenValidated resolves the
            // request's tenant from here, so every query is scoped without touching the subdomain.
            new("tid", employee.TenantId.ToString()),
        };
        // Signals the client to force the "set your own PIN" screen before anything else — the account
        // is still on a temporary PIN. The server also enforces this (set-initial-pin), so the claim is
        // only a UX hint, not the security boundary.
        if (employee.MustChangePin)
            claims.Add(new Claim("mcp", "1"));
        return claims;
    }

    private string Write(List<Claim> claims, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
