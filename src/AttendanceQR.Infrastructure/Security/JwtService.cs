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

    public string GenerateImpersonationToken(Employee employee, Guid impersonatedBy, int expiryMinutes)
    {
        var claims = BaseClaims(employee);
        // Marks this session as a support impersonation and by whom. Advisory — for the log and a client
        // banner. The security boundary is still tid + tv: the token is confined to exactly this
        // employee in this tenant, so it can never reach another company or escalate.
        claims.Add(new Claim("imp", impersonatedBy.ToString()));
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
