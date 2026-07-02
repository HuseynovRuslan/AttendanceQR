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

        var claims = new[]
        {
            new Claim("sub", employee.Id.ToString()),
            new Claim("email", employee.Email),
            new Claim("role", employee.Role.ToString())
        };

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
