using System.Buffers.Text;
using System.Security.Claims;
using System.Text;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Application.Tests;

public class StaticQrGenerationTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly AppDbContext _db;
    private readonly QrTokenService _qr;
    private readonly AdminLocationsController _controller;

    public StaticQrGenerationTests()
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"static-qr-{Guid.NewGuid()}").Options,
            tenant);
        _db.Tenants.Add(new Tenant
        {
            Id = TenantId, Name = "Test", Slug = "test", DisplayName = "Test", IsActive = true,
        });
        _db.Locations.Add(new Location
        {
            Id = _locationId,
            TenantId = TenantId,
            Name = "Çap filialı",
            Latitude = 40.4,
            Longitude = 49.8,
            RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0),
            ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15,
            QrVersion = 3,
            IsActive = true,
        });
        _db.SaveChanges();

        _qr = new QrTokenService(Options.Create(new QrTokenOptions
        {
            Secret = "test-secret-for-static-qr-generation",
            TtlSeconds = 60,
        }));
        _controller = new AdminLocationsController(
            _db, _qr, new OffFaceMatch(), NullLogger<AdminLocationsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("sub", Guid.NewGuid().ToString()),
                        new Claim("role", nameof(EmployeeRole.Admin)),
                    ], "test")),
                },
            },
        };
    }

    [Fact]
    public async Task Omitting_options_preserves_the_existing_60_day_default()
    {
        var before = DateTimeOffset.UtcNow.AddDays(60).ToUnixTimeSeconds();

        var payload = OkValue(await _controller.GenerateStaticQr(_locationId));

        var after = DateTimeOffset.UtcNow.AddDays(60).ToUnixTimeSeconds();
        Assert.False(Property<bool>(payload, "permanent"));
        Assert.Equal(60, Property<int?>(payload, "validityDays"));
        Assert.InRange(TokenExpiry(payload), before, after);
        Assert.True(_qr.Validate(Property<string>(payload, "token")).IsValid);
    }

    [Fact]
    public async Task A_custom_duration_is_signed_into_the_generated_QR()
    {
        var before = DateTimeOffset.UtcNow.AddDays(120).ToUnixTimeSeconds();

        var payload = OkValue(await _controller.GenerateStaticQr(_locationId, validityDays: 120));

        var after = DateTimeOffset.UtcNow.AddDays(120).ToUnixTimeSeconds();
        Assert.False(Property<bool>(payload, "permanent"));
        Assert.Equal(120, Property<int?>(payload, "validityDays"));
        Assert.InRange(TokenExpiry(payload), before, after);
    }

    [Fact]
    public async Task A_permanent_QR_has_no_expiry_date_and_uses_the_signed_zero_sentinel()
    {
        var payload = OkValue(await _controller.GenerateStaticQr(_locationId, permanent: true));
        var token = Property<string>(payload, "token");
        var tokenPayload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));

        Assert.True(Property<bool>(payload, "permanent"));
        Assert.Null(Property<DateTime?>(payload, "expiresAtUtc"));
        Assert.Null(Property<int?>(payload, "validityDays"));
        Assert.Equal("0", tokenPayload.Split('.')[2]);
        Assert.True(_qr.Validate(token).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public async Task A_custom_duration_outside_1_to_3650_days_is_rejected(int days)
    {
        var result = Assert.IsType<BadRequestObjectResult>(
            await _controller.GenerateStaticQr(_locationId, validityDays: days));

        Assert.Equal("QrValidityDaysOutOfRange", Property<string>(result.Value!, "error"));
        Assert.Equal(1, Property<int>(result.Value!, "minValidityDays"));
        Assert.Equal(3650, Property<int>(result.Value!, "maxValidityDays"));
    }

    [Fact]
    public async Task Permanent_and_timed_options_cannot_be_combined()
    {
        var result = Assert.IsType<BadRequestObjectResult>(
            await _controller.GenerateStaticQr(_locationId, permanent: true, validityDays: 30));

        Assert.Equal("QrValidityConflict", Property<string>(result.Value!, "error"));
    }

    private static object OkValue(IActionResult result) => Assert.IsType<OkObjectResult>(result).Value!;

    private static T Property<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static long TokenExpiry(object response)
    {
        var token = Property<string>(response, "token");
        var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        return long.Parse(payload.Split('.')[2]);
    }

    public void Dispose() => _db.Dispose();
}
