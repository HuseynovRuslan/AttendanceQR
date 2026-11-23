using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AttendanceQR.IntegrationTests.Infrastructure;

/// <summary>
/// The real API — Program.cs, every middleware (tenant fail-closed, temp-PIN gate, impersonation boundary,
/// authorization), every controller — in-process, against one test database.
///
/// Production environment on purpose: it is the environment whose behaviour matters (DevController 404s,
/// no developer exception page). Background jobs are removed: an HTTP test must not race the nightly summary
/// or the photo worker; jobs get their own tests that construct them directly.
/// Secrets are test-only values that exist nowhere else.
/// </summary>
public sealed class QrLogApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSigningKey = "integration-tests-only-jwt-signing-key-0123456789-abcdefghijklmnop";
    public const string TestQrSecret = "integration-tests-only-qr-secret-0123456789-abcdefghijklmnop";

    private readonly string _connectionString;
    private readonly string _operatorIds;

    public QrLogApiFactory(string connectionString, IEnumerable<Guid>? operatorEmployeeIds = null)
    {
        _connectionString = connectionString;
        _operatorIds = string.Join(",", operatorEmployeeIds ?? Array.Empty<Guid>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        // UseSetting is host configuration, so Program.cs sees it in the reads it makes BEFORE builder.Build().
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("Jwt:SigningKey", TestJwtSigningKey);
        builder.UseSetting("QrToken:Secret", TestQrSecret);
        builder.UseSetting("App:SuperAdminEmployeeIds", _operatorIds);
        // As production's compose file sets it (docker-compose.prod.yml). With no approval area configured it
        // changes nothing, which is exactly the production state of a company that has not opened one.
        builder.UseSetting("Approvals:Enabled", "true");

        builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>());
    }
}

/// <summary>Small HTTP helpers that talk to the API the way the SPA does: JSON, bearer token, company Origin.</summary>
public static class ApiCalls
{
    public static string OriginFor(string slug) => $"https://{slug}.qrlog.az";

    public static async Task<string> LoginAsync(HttpClient client, string slug, string identifier, string pin)
    {
        var (status, body) = await SendAsync(client, HttpMethod.Post, "/api/auth/login", origin: OriginFor(slug),
            body: new { email = identifier, password = pin });
        Assert.True(status == HttpStatusCode.OK, $"login {identifier}@{slug} → {(int)status} {body}");
        return body.GetProperty("token").GetString()!;
    }

    public static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpClient client, HttpMethod method,
        string path, string? token = null, string? origin = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        JsonElement json;
        try
        {
            json = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            json = JsonDocument.Parse(JsonSerializer.Serialize(new { raw = text })).RootElement.Clone();
        }
        return (response.StatusCode, json);
    }

    public static Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(HttpClient client, string path, string token,
        string? origin = null) => SendAsync(client, HttpMethod.Get, path, token, origin);
}
