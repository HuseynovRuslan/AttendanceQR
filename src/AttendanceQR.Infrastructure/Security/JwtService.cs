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
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new("sub", employee.Id.ToString()),
            new("email", employee.Email),
            new("role", employee.Role.ToString()),
            // Checked against Employee.TokenVersion on every request (Program.cs
            // OnTokenValidated) — lets change-password invalidate every other outstanding token.
            new("tv", employee.TokenVersion.ToString()),
            // Multi-tenancy: which company this session belongs to. OnTokenValidated resolves the
            // request's tenant from here, so every query is scoped without touching the subdomain.
            new("tid", employee.TenantId.ToString())
        };

        // Signals the client to force the "set your own PIN" screen before anything else — the account
        // is still on a temporary PIN. The server also enforces this (set-initial-pin), so the claim is
        // only a UX hint, not the security boundary.
        if (employee.MustChangePin)
            claims.Add(new Claim("mcp", "1"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
