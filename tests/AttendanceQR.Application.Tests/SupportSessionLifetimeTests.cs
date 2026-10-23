using System.IdentityModel.Tokens.Jwt;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Security;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A support session lasts as long as an ordinary login.
///
/// It used to end after sixty minutes. On 03.10.2026 the owner chose otherwise: support is needed
/// often, and an hour-long session cost the operator a fresh sign-in every hour. The lifetime now comes
/// from the one setting every login uses, so the two cannot drift apart again — if ordinary logins are
/// ever shortened, support sessions follow.
/// </summary>
public class SupportSessionLifetimeTests
{
    private static JwtService Jwt(int expiryMinutes) => new(Microsoft.Extensions.Options.Options.Create(new JwtOptions
    {
        Issuer = "qrlog", Audience = "qrlog", SigningKey = new string('k', 48), ExpiryMinutes = expiryMinutes,
    }));

    private static readonly Employee Admin = new()
    {
        Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), FullName = "Musteri Admini",
        Role = EmployeeRole.Admin, PasswordHash = "h",
    };

    private static DateTime ValidTo(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo;

    [Theory]
    [InlineData(false)]   // support: acts as the borrowed admin
    [InlineData(true)]    // «baxış»: read-only, for the customer's group head
    public void A_support_session_lives_as_long_as_an_ordinary_login(bool readOnly)
    {
        var jwt = Jwt(52_560_000);   // the production setting: about a hundred years

        var ordinary = ValidTo(jwt.GenerateToken(Admin));
        var support = ValidTo(jwt.GenerateImpersonationToken(Admin, Guid.NewGuid(), readOnly));

        Assert.InRange((support - ordinary).Duration(), TimeSpan.Zero, TimeSpan.FromMinutes(1));
        Assert.True(support > DateTime.UtcNow.AddYears(50), "a support session must not end after an hour any more");
    }

    [Fact]
    public void Its_lifetime_follows_the_one_setting_every_login_uses()
    {
        var jwt = Jwt(90);

        var support = ValidTo(jwt.GenerateImpersonationToken(Admin, Guid.NewGuid()));

        Assert.InRange(support, DateTime.UtcNow.AddMinutes(89), DateTime.UtcNow.AddMinutes(91));
    }
}
