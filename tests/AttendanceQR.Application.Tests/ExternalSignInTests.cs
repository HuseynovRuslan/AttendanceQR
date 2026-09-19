using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// "QRLog ilə daxil ol" for MEYDAN. What this server vouches for, to whom, signed how — and what it refuses.
/// The receiving app is a fake HTTP handler that records exactly what was sent.
/// </summary>
public class ExternalSignInTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Secret = "shared-vouch-secret-0123456789abcdef-0123456789abcdef";
    private const string ConfirmUrl = "https://meydan.qrlog.az/api/auth/qrlog/confirm";
    private const string Code = "0123456789abcdef0123456789abcdef";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode Answer { get; set; } = HttpStatusCode.NoContent;
        public Exception? Throw { get; set; }
        public HttpRequestMessage? Request { get; private set; }
        public byte[]? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw is not null) throw Throw;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(Answer);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (ExternalSignInController Controller, RecordingHandler App, AppDbContext Db) Build(
        Dictionary<string, string?>? settings = null, bool active = true, bool activated = true, bool impersonating = false,
        string? email = "leyla@example.com", Guid? tenantOfMe = null, string? firstName = "Leyla", string? lastName = "Əsgərova")
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"external-signin-{Guid.NewGuid()}").Options, tenant);
        db.Tenants.Add(new Tenant { Id = TenantId, Name = "Bakı Abadlıq", Slug = "bax", DisplayName = "Bakı Abadlıq", IsActive = true });
        db.Employees.Add(new Employee
        {
            Id = Me, TenantId = tenantOfMe ?? TenantId, FullName = "Leyla Əsgərova", FirstName = firstName, LastName = lastName,
            Email = email, PhoneNumber = "501234567", Role = EmployeeRole.Admin, IsActive = active,
            ActivatedAtUtc = activated ? DateTime.UtcNow.AddDays(-1) : null, PasswordHash = "x",
        });
        db.SaveChanges();

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>
        {
            ["ExternalSignIn:Apps:meydan:VouchSecret"] = Secret,
            ["ExternalSignIn:Apps:meydan:ConfirmUrl"] = ConfirmUrl,
            ["ExternalSignIn:Apps:meydan:AnyTenant"] = "true",
        }).Build();

        var handler = new RecordingHandler();
        var claims = new List<Claim> { new("sub", Me.ToString()), new("role", nameof(EmployeeRole.Admin)), new("tid", TenantId.ToString()) };
        if (impersonating) claims.Add(new Claim("imp", Guid.NewGuid().ToString()));
        var controller = new ExternalSignInController(db, new Factory(handler), config, NullLogger<ExternalSignInController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            },
        };
        return (controller, handler, db);
    }

    private static int Status(IActionResult result) => result switch
    {
        NoContentResult => 204,
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(result.GetType().Name),
    };

    [Fact]
    public async Task Vouches_for_the_signed_in_employee_with_a_stable_subject_and_only_the_minimum_identity()
    {
        var (controller, app, db) = Build();
        using (db)
        {
            var result = await controller.Confirm("meydan", new ExternalSignInController.ConfirmRequest(Code), CancellationToken.None);
            Assert.Equal(204, Status(result));
        }

        Assert.NotNull(app.Request);
        Assert.Equal(ConfirmUrl, app.Request!.RequestUri!.ToString());
        Assert.Equal("application/json", app.Request.Content!.Headers.ContentType!.MediaType);
        var json = JsonDocument.Parse(app.Body!).RootElement;
        Assert.Equal(Code, json.GetProperty("code").GetString());
        Assert.Equal(Me.ToString(), json.GetProperty("subject").GetString());
        Assert.Equal("Leyla Əsgərova", json.GetProperty("fullName").GetString());
        Assert.Equal("Leyla", json.GetProperty("firstName").GetString());
        Assert.Equal("Əsgərova", json.GetProperty("lastName").GetString());
        Assert.Equal("leyla@example.com", json.GetProperty("email").GetString());
        Assert.False(json.GetProperty("emailVerified").GetBoolean());
        // Nothing the other app has no business knowing.
        var text = Encoding.UTF8.GetString(app.Body!);
        Assert.DoesNotContain("501234567", text);
        Assert.DoesNotContain("phone", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Admin", text);
        Assert.DoesNotContain(Secret, text);

        // Signed over exactly the bytes sent, keyed with the shared secret, with a fresh timestamp.
        var timestamp = app.Request.Headers.GetValues(ExternalSignInController.TimestampHeader).Single();
        var signature = app.Request.Headers.GetValues(ExternalSignInController.SignatureHeader).Single();
        var sent = DateTimeOffset.Parse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind);
        Assert.True((DateTimeOffset.UtcNow - sent).Duration() < TimeSpan.FromMinutes(1));
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes(timestamp + "\n").Concat(app.Body!).ToArray())).ToLowerInvariant();
        Assert.Equal(expected, signature);
        Assert.Equal(64, signature.Length);
    }

    [Fact]
    public async Task An_employee_without_an_e_mail_is_vouched_for_with_a_null_address()
    {
        var (controller, app, db) = Build(email: null);
        using (db)
            Assert.Equal(204, Status(await controller.Confirm("meydan", new(Code), CancellationToken.None)));
        var json = JsonDocument.Parse(app.Body!).RootElement;
        Assert.Equal(JsonValueKind.Null, json.GetProperty("email").ValueKind);
        Assert.Equal(Me.ToString(), json.GetProperty("subject").GetString());
    }

    [Theory]
    [InlineData("kitabxana")]      // another name, not configured here
    [InlineData("MEYDAN")]         // names are lower-case
    [InlineData("../meydan")]
    [InlineData("meydan:x")]
    public async Task An_app_that_is_not_configured_does_not_exist(string app)
    {
        var (controller, handler, db) = Build();
        using (db)
            Assert.Equal(404, Status(await controller.Confirm(app, new(Code), CancellationToken.None)));
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData("VouchSecret")]
    [InlineData("ConfirmUrl")]
    [InlineData("AnyTenant")]
    public async Task A_half_configured_app_answers_not_configured_and_vouches_for_nobody(string missing)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ExternalSignIn:Apps:meydan:VouchSecret"] = Secret,
            ["ExternalSignIn:Apps:meydan:ConfirmUrl"] = ConfirmUrl,
            ["ExternalSignIn:Apps:meydan:AnyTenant"] = "true",
        };
        settings[$"ExternalSignIn:Apps:meydan:{missing}"] = missing == "AnyTenant" ? "false" : "";
        var (controller, handler, db) = Build(settings);
        using (db)
            Assert.Equal(503, Status(await controller.Confirm("meydan", new(Code), CancellationToken.None)));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task A_plain_http_confirm_url_is_never_used()
    {
        var (controller, handler, db) = Build(new Dictionary<string, string?>
        {
            ["ExternalSignIn:Apps:meydan:VouchSecret"] = Secret,
            ["ExternalSignIn:Apps:meydan:ConfirmUrl"] = "http://meydan.qrlog.az/api/auth/qrlog/confirm",
            ["ExternalSignIn:Apps:meydan:AnyTenant"] = "true",
        });
        using (db)
            Assert.Equal(503, Status(await controller.Confirm("meydan", new(Code), CancellationToken.None)));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task Only_listed_companies_when_not_open_to_every_tenant()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ExternalSignIn:Apps:meydan:VouchSecret"] = Secret,
            ["ExternalSignIn:Apps:meydan:ConfirmUrl"] = ConfirmUrl,
            ["ExternalSignIn:Apps:meydan:TenantIds:0"] = OtherTenantId.ToString(),
        };
        var (controller, handler, db) = Build(settings);
        using (db)
            Assert.Equal(403, Status(await controller.Confirm("meydan", new(Code), CancellationToken.None)));
        Assert.Null(handler.Request);

        settings["ExternalSignIn:Apps:meydan:TenantIds:0"] = TenantId.ToString();
        var (allowed, handler2, db2) = Build(settings);
        using (db2)
            Assert.Equal(204, Status(await allowed.Confirm("meydan", new(Code), CancellationToken.None)));
        Assert.NotNull(handler2.Request);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789abcdef0123456789abcdeg")]   // not hex
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef01")] // too long
    public async Task A_malformed_code_is_refused_before_anything_is_sent(string code)
    {
        var (controller, handler, db) = Build();
        using (db)
            Assert.Equal(400, Status(await controller.Confirm("meydan", new(code), CancellationToken.None)));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task An_impersonation_session_cannot_sign_in_elsewhere_as_the_borrowed_person()
    {
        var (controller, handler, db) = Build(impersonating: true);
        using (db)
            Assert.Equal(403, Status(await controller.Confirm("meydan", new(Code), CancellationToken.None)));
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(false, true)]  // deactivated
    [InlineData(true, false)]  // never activated
    public async Task Only_a_live_activated_account_is_vouched_for(bool active, bool activated)
    {
        var (controller, handler, db) = Build(active: active, activated: activated);
        using (db)
            Assert.Equal(401, Status(await controller.Confirm("meydan", new(Code), CancellationToken.None)));
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task The_apps_answers_are_translated_for_the_phone()
    {
        var (expired, h1, db1) = Build();
        h1.Answer = HttpStatusCode.Conflict;
        using (db1)
            Assert.Equal(409, Status(await expired.Confirm("meydan", new(Code), CancellationToken.None)));

        var (refused, h2, db2) = Build();
        h2.Answer = HttpStatusCode.Unauthorized;
        using (db2)
            Assert.Equal(502, Status(await refused.Confirm("meydan", new(Code), CancellationToken.None)));

        var (down, h3, db3) = Build();
        h3.Throw = new HttpRequestException("connection refused");
        using (db3)
            Assert.Equal(502, Status(await down.Confirm("meydan", new(Code), CancellationToken.None)));
    }

    [Fact]
    public void The_signature_is_deterministic_and_keyed()
    {
        var body = Encoding.UTF8.GetBytes("{\"code\":\"x\"}");
        var a = ExternalSignInController.Sign(Secret, "2026-09-19T00:00:00.0000000+00:00", body);
        var b = ExternalSignInController.Sign(Secret, "2026-09-19T00:00:00.0000000+00:00", body);
        var other = ExternalSignInController.Sign("another-secret", "2026-09-19T00:00:00.0000000+00:00", body);
        var later = ExternalSignInController.Sign(Secret, "2026-09-19T00:00:01.0000000+00:00", body);
        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
        Assert.NotEqual(a, later);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }
}
