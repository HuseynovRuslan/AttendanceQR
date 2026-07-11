using System.Security.Claims;
using System.Security.Cryptography;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/employees")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly InvitationOptions _invitationOptions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginLockoutStore _lockout;

    public AdminController(
        AppDbContext db,
        IOptions<InvitationOptions> invitationOptions,
        IPasswordHasher passwordHasher,
        ILoginLockoutStore lockout)
    {
        _db = db;
        _invitationOptions = invitationOptions.Value;
        _passwordHasher = passwordHasher;
        _lockout = lockout;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        // Admin accounts are system operators, not on-site staff to manage here — hide them
        // entirely (they don't check in/out and aren't part of the "İşçilər" roster).
        var employees = await _db.Employees
            .Include(e => e.DeviceBindings)
            .Where(e => e.Role != EmployeeRole.Admin)
            .OrderBy(e => e.FullName)
            .ToListAsync(HttpContext.RequestAborted);

        var locationNames = await _db.Locations
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        var result = employees.Select(e =>
        {
            // An employee may hold several contexts (Safari, the installed PWA). The list still shows
            // one label — the most recently used — plus how many are bound in total.
            var active = e.DeviceBindings.Where(d => d.IsActive).OrderByDescending(d => d.LastSeenAtUtc).ToList();
            var newest = active.FirstOrDefault();
            return new
            {
                id = e.Id,
                fullName = e.FullName,
                fatherName = e.FatherName,
                position = e.Position,
                birthYear = e.BirthYear,
                email = e.Email,
                role = e.Role.ToString(),
                phoneNumber = e.PhoneNumber,
                locationId = e.LocationId,
                locationName = locationNames.GetValueOrDefault(e.LocationId),
                isActive = e.IsActive,
                activated = e.ActivatedAtUtc != null,
                hasDevice = newest != null,
                deviceLabel = newest?.DeviceLabel,
                boundAtUtc = newest?.BoundAtUtc,
                deviceCount = active.Count,
                createdAtUtc = e.CreatedAtUtc
            };
        });
        return Ok(result);
    }

    // Photo audit: clear ONE employee's reference selfie so their next check-in re-seeds it with the
    // correct face. Needed because the reference is auto-seeded from the first check-in photo — if
    // that first scan was an admin's (their face), the reference is wrong. Nulling the key is enough:
    // the next check-in overwrites the object at reference/{id}.
    [HttpPost("{id:guid}/reset-reference-photo")]
    public async Task<IActionResult> ResetReferencePhoto(Guid id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, HttpContext.RequestAborted);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        employee.ReferencePhotoKey = null;
        employee.ReferencePhotoTakenAtUtc = null;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { id = employee.Id });
    }

    // Bulk version — clears every employee's reference selfie in one shot (e.g. all references were
    // seeded from the admin's face during setup). Each re-seeds on that employee's next check-in.
    [HttpPost("reset-all-reference-photos")]
    public async Task<IActionResult> ResetAllReferencePhotos()
    {
        var employees = await _db.Employees
            .Where(e => e.ReferencePhotoKey != null)
            .ToListAsync(HttpContext.RequestAborted);
        foreach (var e in employees)
        {
            e.ReferencePhotoKey = null;
            e.ReferencePhotoTakenAtUtc = null;
        }
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { reset = employees.Count });
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteRequest request)
    {
        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var (takenEmails, takenPhones) = await LoadTakenIdentifiersAsync();
        var (employee, token, error) = BuildInvite(
            request.FullName, request.Email, request.PhoneNumber, request.FatherName, request.Position,
            request.BirthYear, request.LocationId, request.Role, takenEmails, takenPhones);

        if (error is not null)
            return error is "EmailAlreadyExists" or "PhoneAlreadyExists"
                ? Conflict(new { error })
                : BadRequest(new { error });

        _db.Employees.Add(employee!);
        await _db.SaveChangesAsync();

        // No email/SMS channel yet — return the PLAINTEXT token so it can be shared by hand.
        // (Base64Url is URL-safe, so it needs no additional encoding in the link.)
        return Ok(new
        {
            employeeId = employee!.Id,
            activationToken = token,
            activationUrl = $"/activate?token={token}"
        });
    }

    // POST /api/admin/employees/bulk-invite — add many employees at once (one shared location + role).
    // Each row is validated on its own: a duplicate phone or missing name is reported back in `failed`
    // without blocking the others. All the good rows are saved in a single transaction.
    [HttpPost("bulk-invite")]
    public async Task<IActionResult> BulkInvite([FromBody] BulkInviteRequest request)
    {
        if (request.Rows is null || request.Rows.Count == 0)
            return BadRequest(new { error = "NoRows" });
        if (request.Rows.Count > 200)
            return BadRequest(new { error = "TooManyRows" });
        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var (takenEmails, takenPhones) = await LoadTakenIdentifiersAsync();
        var created = new List<object>();
        var failed = new List<object>();

        foreach (var row in request.Rows)
        {
            var (employee, token, error) = BuildInvite(
                row.FullName, row.Email, row.PhoneNumber, fatherName: null, row.Position, birthYear: null,
                request.LocationId, request.Role, takenEmails, takenPhones);

            if (error is not null)
            {
                failed.Add(new { fullName = row.FullName, error });
                continue;
            }

            _db.Employees.Add(employee!);
            created.Add(new
            {
                employeeId = employee!.Id,
                fullName = employee.FullName,
                phoneNumber = employee.PhoneNumber,
                activationToken = token,
                activationUrl = $"/activate?token={token}"
            });
        }

        if (created.Count > 0)
            await _db.SaveChangesAsync();

        return Ok(new { createdCount = created.Count, failedCount = failed.Count, created, failed });
    }

    // Emails + phones already in use, as sets, so a batch can check for collisions in memory (against
    // the DB and against earlier rows in the same batch) without a query per row.
    private async Task<(HashSet<string> Emails, HashSet<string> Phones)> LoadTakenIdentifiersAsync()
    {
        var emails = await _db.Employees.Select(e => e.Email).ToListAsync();
        var phones = await _db.Employees.Where(e => e.PhoneNumber != null).Select(e => e.PhoneNumber!).ToListAsync();
        return (new HashSet<string>(emails, StringComparer.Ordinal), new HashSet<string>(phones, StringComparer.Ordinal));
    }

    // Builds one invited employee (not yet added to the context) + its activation token, or returns an
    // error code. Mutates the taken-sets so the next call in a batch sees this row's identifiers. Shared
    // by the single and bulk invite paths so their validation can never drift apart.
    private (Employee? Employee, string? Token, string? Error) BuildInvite(
        string fullName, string? emailIn, string? phoneIn, string? fatherName, string? position, int? birthYear,
        Guid locationId, EmployeeRole role, HashSet<string> takenEmails, HashSet<string> takenPhones)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null, "NameRequired");

        var phone = PhoneNumbers.Normalize(phoneIn);
        var hasEmail = !string.IsNullOrWhiteSpace(emailIn);

        // At least one login identifier so the employee can sign in later (phone OR email).
        if (!hasEmail && phone is null)
            return (null, null, "NeedEmailOrPhone");

        // Email stays non-null (it's a JWT claim); synthesize a unique placeholder when only a phone
        // was given. Login works by either identifier.
        var email = hasEmail ? emailIn!.Trim() : $"emp-{Guid.NewGuid().ToString("N")[..10]}@baki.local";

        if (takenEmails.Contains(email))
            return (null, null, "EmailAlreadyExists");
        if (phone is not null && takenPhones.Contains(phone))
            return (null, null, "PhoneAlreadyExists");

        var employee = new Employee
        {
            FullName = fullName.Trim(),
            Email = email,
            PhoneNumber = phone,
            FatherName = string.IsNullOrWhiteSpace(fatherName) ? null : fatherName.Trim(),
            Position = string.IsNullOrWhiteSpace(position) ? null : position.Trim(),
            BirthYear = birthYear,
            LocationId = locationId,
            Role = role,
            PasswordHash = string.Empty,       // set by the employee at activation
            IsActive = true,
            ActivatedAtUtc = null,             // not activated yet
            InvitationExpiresUtc = DateTime.UtcNow.AddHours(_invitationOptions.ExpiryHours)
        };

        // The token embeds the (non-secret) employee id so activation can look the account up by a key
        // that survives activation; only the random part's hash is stored.
        var (activationToken, randomHash) = ActivationToken.Create(employee.Id);
        employee.InvitationTokenHash = randomHash;

        takenEmails.Add(email);
        if (phone is not null) takenPhones.Add(phone);

        return (employee, activationToken, null);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeUpdateRequest request)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        var phone = PhoneNumbers.Normalize(request.PhoneNumber);
        // Keep the current email if none supplied, so a phone-only edit doesn't wipe it.
        var email = string.IsNullOrWhiteSpace(request.Email) ? employee.Email : request.Email.Trim();

        if (await _db.Employees.AnyAsync(e => e.Email == email && e.Id != id))
            return Conflict(new { error = "EmailAlreadyExists" });
        if (phone is not null && await _db.Employees.AnyAsync(e => e.PhoneNumber == phone && e.Id != id))
            return Conflict(new { error = "PhoneAlreadyExists" });

        employee.FullName = request.FullName;
        employee.Email = email;
        employee.PhoneNumber = phone;
        employee.FatherName = request.FatherName;
        employee.Position = request.Position;
        employee.BirthYear = request.BirthYear;
        employee.LocationId = request.LocationId;
        employee.Role = request.Role;
        employee.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new { id = employee.Id });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool force = false)
    {
        Guid.TryParse(User.FindFirstValue("sub"), out var requesterId);
        if (id == requesterId)
            return BadRequest(new { error = "CannotDeleteSelf" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        // Attendance/summary/device-change FKs are Restrict — refuse to delete an employee with
        // history (it would fail at the DB anyway) unless the caller explicitly opts into a
        // force delete (e.g. wiping a test account), which purges that history first.
        var hasHistory = await _db.AttendanceRecords.AnyAsync(a => a.EmployeeId == id)
                         || await _db.DailySummaries.AnyAsync(d => d.EmployeeId == id)
                         || await _db.DeviceChangeRequests.AnyAsync(r => r.EmployeeId == id || r.ReviewedByEmployeeId == id);
        if (hasHistory && !force)
            return Conflict(new { error = "EmployeeHasHistory" });

        if (hasHistory)
        {
            await _db.AttendanceRecords.Where(a => a.EmployeeId == id).ExecuteDeleteAsync();
            await _db.DailySummaries.Where(d => d.EmployeeId == id).ExecuteDeleteAsync();
            // Own requests are this employee's history — remove them. Requests they merely
            // reviewed belong to someone else's history — keep the request, just anonymize
            // the reviewer (mirrors the AuditLogs SetNull behavior on employee delete).
            await _db.DeviceChangeRequests.Where(r => r.EmployeeId == id).ExecuteDeleteAsync();
            await _db.DeviceChangeRequests.Where(r => r.ReviewedByEmployeeId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReviewedByEmployeeId, (Guid?)null));
        }

        // DeviceBinding and ManagedLocations cascade; AuditLogs are set null.
        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = id, forced = hasHistory && force });
    }

    // Testing/reset helper: clears an employee's check-in/check-out history so the same account +
    // device can be used to test the scan flow again from a clean slate. Keeps the employee,
    // activation state and device binding untouched — only attendance data is removed.
    [HttpPost("{id:guid}/reset-attendance")]
    public async Task<IActionResult> ResetAttendance(Guid id)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == id))
            return NotFound(new { error = "EmployeeNotFound" });

        var recordsDeleted = await _db.AttendanceRecords.Where(a => a.EmployeeId == id).ExecuteDeleteAsync();
        var summariesDeleted = await _db.DailySummaries.Where(d => d.EmployeeId == id).ExecuteDeleteAsync();
        return Ok(new { attendanceRecordsDeleted = recordsDeleted, summariesDeleted });
    }

    // Regenerate the activation link for a not-yet-activated employee (e.g. the original link was
    // lost or expired). Only the new token's hash is stored; the plaintext is returned once.
    [HttpPost("{id:guid}/reinvite")]
    public async Task<IActionResult> Reinvite(Guid id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });
        if (employee.ActivatedAtUtc is not null)
            return Conflict(new { error = "AlreadyActivated" });

        var (activationToken, randomHash) = ActivationToken.Create(employee.Id);
        employee.InvitationTokenHash = randomHash;
        employee.InvitationExpiresUtc = DateTime.UtcNow.AddHours(_invitationOptions.ExpiryHours);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            employeeId = employee.Id,
            activationToken,
            activationUrl = $"/activate?token={activationToken}"
        });
    }

    // POST /api/admin/employees/{id}/reset-pin — set a random temporary PIN for an activated employee
    // who forgot theirs (a hashed PIN can never be read back). Returns the plaintext temp PIN so the
    // admin can pass it on; the employee logs in and changes it from the menu. Also clears any login
    // lockout so they can sign in straight away. Not-yet-activated accounts use reinvite instead.
    [HttpPost("{id:guid}/reset-pin")]
    public async Task<IActionResult> ResetPin(Guid id)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });
        if (employee.ActivatedAtUtc is null)
            return Conflict(new { error = "NotActivated" });

        // Cryptographically random 4-digit PIN, zero-padded (0000–9999).
        var pin = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
        employee.PasswordHash = _passwordHasher.Hash(pin);
        await _db.SaveChangesAsync();

        // Clear the lockout under every identifier they might have typed (the store keys by the raw
        // string, so a phone with and without the leading 0 are distinct buckets).
        _lockout.RecordSuccess(employee.Email);
        if (employee.PhoneNumber is not null)
        {
            _lockout.RecordSuccess(employee.PhoneNumber);
            _lockout.RecordSuccess("0" + employee.PhoneNumber);
        }

        return Ok(new { tempPin = pin });
    }
}
