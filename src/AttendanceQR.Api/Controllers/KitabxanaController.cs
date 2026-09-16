using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Vouches for the signed-in employee to Kitabxana 2.0 (book.qrlog.az), so they can start a quiz
/// without typing their name and phone at its kiosk.
///
/// The employee's own token is what proves who they are: this endpoint takes a code and nothing else,
/// and reads the name and phone from their staff record. The signature that goes out is computed
/// here, on the server, because the secret it is keyed with must never exist on a phone - anyone
/// holding it could claim to be any employee.
///
/// Fail-closed, like the rest of this codebase: unconfigured means "not for this company", never
/// "for all of them".
/// </summary>
[ApiController]
[Route("api/kitabxana")]
[Authorize]
public class KitabxanaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<KitabxanaController> _logger;

    public KitabxanaController(
        AppDbContext db,
        IHttpClientFactory http,
        IConfiguration config,
        ILogger<KitabxanaController> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
    }

    /// <summary>The code the kiosk put in its QR. Nothing identifying travels with it.</summary>
    public sealed record SignInRequest(string Code);

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request, CancellationToken ct)
    {
        var secret = _config["Kitabxana:VouchSecret"];
        var baseUrl = (_config["Kitabxana:BaseUrl"] ?? "https://book.qrlog.az").TrimEnd('/');
        var allowedTenants = _config.GetSection("Kitabxana:TenantIds").Get<Guid[]>() ?? [];
        // The quiz is open to anyone who types their name and phone into its kiosk, so limiting the
        // shortcut to one company protected nothing and only made the two routes in inconsistent.
        // Still an explicit opt-in, not a default: somebody has to write it down.
        var anyTenant = _config.GetValue("Kitabxana:AnyTenant", false);

        if (string.IsNullOrWhiteSpace(secret) || (!anyTenant && allowedTenants.Length == 0))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "NotConfigured" });
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length is < 8 or > 64 || !code.All(Uri.IsHexDigit))
        {
            return BadRequest(new { error = "InvalidCode" });
        }

        var employeeId = User.EmployeeId();
        var employee = await _db.Employees
            .AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.FullName, e.PhoneNumber, e.TenantId })
            .FirstOrDefaultAsync(ct);

        if (employee is null)
        {
            return Unauthorized(new { error = "UnknownEmployee" });
        }

        // Whose employees the quiz is for. With AnyTenant it is everyone on this system.
        if (!anyTenant && !allowedTenants.Contains(employee.TenantId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotEligible" });
        }

        // The quiz identifies a player by phone number - its one-attempt-per-campaign rule is keyed on
        // exactly that - so without one there is nothing to sign them in as.
        if (string.IsNullOrWhiteSpace(employee.PhoneNumber))
        {
            return BadRequest(new { error = "NoPhoneNumber" });
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{code}\n{employee.PhoneNumber}\n{timestamp}")));

        try
        {
            var client = _http.CreateClient("kitabxana");
            using var response = await client.PostAsJsonAsync($"{baseUrl}/api/qrlog-login/confirm", new
            {
                code,
                fullName = employee.FullName,
                phoneNumber = employee.PhoneNumber,
                timestamp,
                signature,
            }, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // The QR on the kiosk has expired or was already used; a fresh one fixes it.
                return Conflict(new { error = "CodeExpired" });
            }

            if (!response.IsSuccessStatusCode)
            {
                // Never log the secret, the signature or the phone number.
                _logger.LogWarning(
                    "Kitabxana sign-in refused for employee {EmployeeId} with status {Status}.",
                    employeeId, (int)response.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { error = "KitabxanaRefused" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Kitabxana sign-in could not be delivered for employee {EmployeeId} ({ExceptionType}).",
                employeeId, ex.GetType().Name);
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "KitabxanaUnreachable" });
        }

        return NoContent();
    }
}
