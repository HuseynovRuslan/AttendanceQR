using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// "QRLog ilə daxil ol" for other applications of ours (today: PRIZMA, prizma.qrlog.az). QRLog is not an OpenID
/// provider; this is the same shape as the Kitabxana sign-in, generalised: the other app opens a ticket and sends
/// the person here with its code, the person approves on this signed-in phone with one tap, and THIS SERVER posts
/// the identity to the app over a back channel, signed with the secret the two share. The phone never holds the
/// secret and the browser never carries a token — only the random ticket code travels through the address bar.
///
/// What is vouched for is deliberately small: a stable identifier (the employee id — never a PRIZMA key), the name
/// and, if the record has one, the e-mail address, flagged as unverified because this system does not verify
/// addresses. Never the phone number, the company, the role or anything else on the staff record.
///
/// Per app, in configuration (ExternalSignIn:Apps:&lt;app&gt;): VouchSecret, ConfirmUrl (https), and either
/// AnyTenant=true or TenantIds. Fail-closed: an app that is not configured, or is half configured, does not exist.
/// </summary>
[ApiController]
[Route("api/external-signin")]
[Authorize]
public partial class ExternalSignInController : ControllerBase
{
    public const string TimestampHeader = "X-QRLog-Timestamp";
    public const string SignatureHeader = "X-QRLog-Signature";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ExternalSignInController> _logger;

    public ExternalSignInController(
        AppDbContext db,
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<ExternalSignInController> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>The code the other app put in its link. Nothing identifying travels with it.</summary>
    public sealed record ConfirmRequest(string Code);

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$")]
    private static partial Regex AppName();

    [HttpPost("{app}/confirm")]
    public async Task<IActionResult> Confirm(string app, [FromBody] ConfirmRequest request, CancellationToken ct)
    {
        if (!AppName().IsMatch(app))
            return NotFound(new { error = "UnknownApp" });

        var section = _config.GetSection($"ExternalSignIn:Apps:{app}");
        var secret = section["VouchSecret"];
        var confirmUrl = section["ConfirmUrl"];
        var anyTenant = section.GetValue("AnyTenant", false);
        var allowedTenants = section.GetSection("TenantIds").Get<Guid[]>() ?? [];
        if (!section.Exists())
            return NotFound(new { error = "UnknownApp" });
        if (string.IsNullOrWhiteSpace(secret) || !IsHttpsUrl(confirmUrl) || (!anyTenant && allowedTenants.Length == 0))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "NotConfigured" });

        // An operator borrowing a customer's admin must not be able to sign in elsewhere as that person.
        if (User.IsImpersonating())
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });

        var code = request.Code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (code.Length is < 16 or > 64 || !code.All(Uri.IsHexDigit))
            return BadRequest(new { error = "InvalidCode" });

        var employeeId = User.EmployeeId();
        var employee = await _db.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.TenantId, e.FullName, e.FirstName, e.LastName, e.Email, e.IsActive, e.ActivatedAtUtc })
            .FirstOrDefaultAsync(ct);
        // Only a live, activated account is vouched for — the same bar the login itself sets.
        if (employee is null || !employee.IsActive || employee.ActivatedAtUtc is null)
            return Unauthorized(new { error = "UnknownEmployee" });
        if (!anyTenant && !allowedTenants.Contains(employee.TenantId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotEligible" });

        var email = string.IsNullOrWhiteSpace(employee.Email) ? null : employee.Email.Trim();
        // The exact bytes that are signed are the exact bytes that are sent.
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            code,
            subject = employee.Id,
            fullName = employee.FullName,
            firstName = employee.FirstName,
            lastName = employee.LastName,
            email,
            emailVerified = false,
        }, Json);
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = Sign(secret, timestamp, body);

        try
        {
            var client = _http.CreateClient("external-signin");
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            using var message = new HttpRequestMessage(HttpMethod.Post, confirmUrl) { Content = content };
            message.Headers.Add(TimestampHeader, timestamp);
            message.Headers.Add(SignatureHeader, signature);
            using var response = await client.SendAsync(message, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The ticket ran out or was already used; a fresh one from the app fixes it.
                return Conflict(new { error = "CodeExpired" });
            }
            if (!response.IsSuccessStatusCode)
            {
                // Status only: never the secret, the signature, the address or the body.
                _logger.LogWarning("External sign-in to {App} refused for employee {EmployeeId} with status {Status}.",
                    app, employeeId, (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "AppRefused" });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("External sign-in to {App} could not be delivered for employee {EmployeeId} ({ExceptionType}).",
                app, employeeId, ex.GetType().Name);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "AppUnreachable" });
        }

        _logger.LogInformation("Employee {EmployeeId} signed in to {App}.", employeeId, app);
        return NoContent();
    }

    /// <summary>Lower-case hex HMAC-SHA256 over timestamp + "\n" + body — what the receiving app verifies.</summary>
    public static string Sign(string secret, string timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(timestamp + "\n");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed)).ToLowerInvariant();
    }

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
