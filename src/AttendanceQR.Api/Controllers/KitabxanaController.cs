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

        // We store nine national digits ("501234567"); the quiz files everyone under "+994XXXXXXXXX"
        // and refuses anything it cannot read that way. Sending ours as-is failed every single sign-in,
        // and because the quiz's form is hidden behind the QR, nobody saw why - the button simply did
        // nothing. Normalising here also means an employee who once typed their number in by hand
        // lands on the SAME participant, which is what keeps one attempt per campaign honest.
        if (!TryNormalizePhone(employee.PhoneNumber, out var phone))
        {
            return BadRequest(new { error = "BadPhoneNumber" });
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{code}\n{phone}\n{timestamp}")));

        try
        {
            var client = _http.CreateClient("kitabxana");
            using var response = await client.PostAsJsonAsync($"{baseUrl}/api/qrlog-login/confirm", new
            {
                code,
                fullName = employee.FullName,
                phoneNumber = phone,
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

    /// <summary>
    /// To "+994XXXXXXXXX", the one form the quiz files participants under. Accepts what this database
    /// holds (nine national digits) as well as the 0XX / 994 / +994 spellings, and ignores the spaces,
    /// dashes and brackets people type into a phone field.
    /// </summary>
    private static bool TryNormalizePhone(string input, out string normalized)
    {
        normalized = string.Empty;
        var digits = new string(input.Where(char.IsDigit).ToArray());

        // Longest prefix first: "994..." must not be read as a national number beginning with 99.
        if (digits.Length == 12 && digits.StartsWith("994", StringComparison.Ordinal)) digits = digits[3..];
        else if (digits.Length == 10 && digits[0] == '0') digits = digits[1..];

        if (digits.Length != 9) return false;
        // The quiz only knows Azerbaijani mobiles; a landline would be signed in as somebody who then
        // cannot play, which is worse than being told now.
        if (!MobilePrefixes.Contains(digits[..2])) return false;

        normalized = "+994" + digits;
        return true;
    }

    private static readonly string[] MobilePrefixes = ["10", "50", "51", "55", "60", "70", "77", "99"];
}
