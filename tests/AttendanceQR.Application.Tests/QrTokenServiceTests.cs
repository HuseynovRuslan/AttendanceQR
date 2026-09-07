using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AttendanceQR.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Application.Tests;

public class QrTokenServiceTests
{
    private static readonly Guid LocationId = Guid.Parse("9c167947-1fc5-4a3c-a5c0-c88b9378d642");
    private const string Secret = "test-secret-for-exact-expired-poster-exemption";

    [Fact]
    public void An_expired_token_is_rejected_without_an_exemption()
    {
        var service = Service();
        var token = service.Generate(LocationId, version: 4, ttlSeconds: -60);

        var result = service.Validate(token);

        Assert.False(result.IsValid);
        Assert.Equal("TokenExpired", result.FailureReason);
    }

    [Fact]
    public void The_exact_fingerprinted_expired_poster_is_accepted()
    {
        var token = Service().Generate(LocationId, version: 4, ttlSeconds: -60);
        var service = Service(Hash(token), LocationId);

        var result = service.Validate(token);

        Assert.True(result.IsValid);
        Assert.Equal(LocationId, result.LocationId);
        Assert.Equal(4, result.Version);
    }

    [Fact]
    public void Another_expired_token_for_the_same_location_and_version_stays_expired()
    {
        var issuer = Service();
        var exemptPoster = issuer.Generate(LocationId, version: 4, ttlSeconds: -60);
        var otherToken = issuer.Generate(LocationId, version: 4, ttlSeconds: -60);
        var service = Service(Hash(exemptPoster), LocationId);

        var result = service.Validate(otherToken);

        Assert.False(result.IsValid);
        Assert.Equal("TokenExpired", result.FailureReason);
    }

    [Fact]
    public void A_matching_fingerprint_does_not_exempt_a_different_location()
    {
        var token = Service().Generate(LocationId, version: 4, ttlSeconds: -60);
        var service = Service(Hash(token), Guid.NewGuid());

        var result = service.Validate(token);

        Assert.False(result.IsValid);
        Assert.Equal("TokenExpired", result.FailureReason);
    }

    [Fact]
    public void A_tampered_token_is_rejected_before_its_matching_fingerprint_is_considered()
    {
        var token = Service().Generate(LocationId, version: 4, ttlSeconds: -60);
        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        var parts = payload.Split('.');
        parts[3] = (parts[3][0] == 'A' ? 'B' : 'A') + parts[3][1..];
        var tampered = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Join('.', parts)));
        var service = Service(Hash(tampered), LocationId);

        var result = service.Validate(tampered);

        Assert.False(result.IsValid);
        Assert.Equal("SignatureInvalid", result.FailureReason);
    }

    [Fact]
    public void A_permanent_token_has_a_signed_zero_sentinel_and_validates()
    {
        var service = Service();
        var token = service.GeneratePermanent(LocationId, version: 7);

        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        var result = service.Validate(token);

        Assert.Equal("0", payload.Split('.')[2]);
        Assert.True(result.IsValid);
        Assert.Equal(LocationId, result.LocationId);
        Assert.Equal(7, result.Version);
    }

    [Fact]
    public void Changing_an_expired_token_to_the_permanent_sentinel_breaks_its_signature()
    {
        var service = Service();
        var token = service.Generate(LocationId, version: 4, ttlSeconds: -60);
        var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split('.');
        parts[2] = "0";
        var tampered = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Join('.', parts)));

        var result = service.Validate(tampered);

        Assert.False(result.IsValid);
        Assert.Equal("SignatureInvalid", result.FailureReason);
    }

    private static QrTokenService Service(string hash = "", Guid? locationId = null) =>
        new(Options.Create(new QrTokenOptions
        {
            Secret = Secret,
            TtlSeconds = 60,
            ExpiryExemptTokenSha256 = hash,
            ExpiryExemptLocationId = locationId,
        }));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
