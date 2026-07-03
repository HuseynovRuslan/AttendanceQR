using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
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

    public AdminController(AppDbContext db, IOptions<InvitationOptions> invitationOptions)
    {
        _db = db;
        _invitationOptions = invitationOptions.Value;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var employees = await _db.Employees
            .Include(e => e.DeviceBinding)
            .OrderBy(e => e.FullName)
            .ToListAsync(HttpContext.RequestAborted);

        var locationNames = await _db.Locations
            .ToDictionaryAsync(l => l.Id, l => l.Name, HttpContext.RequestAborted);

        var result = employees.Select(e => new
        {
            id = e.Id,
            fullName = e.FullName,
            fatherName = e.FatherName,
            position = e.Position,
            birthYear = e.BirthYear,
            email = e.Email,
            role = e.Role.ToString(),
            locationId = e.LocationId,
            locationName = locationNames.GetValueOrDefault(e.LocationId),
            isActive = e.IsActive,
            activated = e.ActivatedAtUtc != null,
            hasDevice = e.DeviceBinding != null && e.DeviceBinding.IsActive,
            boundAtUtc = e.DeviceBinding != null ? e.DeviceBinding.BoundAtUtc : (DateTime?)null,
            createdAtUtc = e.CreatedAtUtc
        });
        return Ok(result);
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite([FromBody] InviteRequest request)
    {
        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        if (await _db.Employees.AnyAsync(e => e.Email == request.Email))
            return Conflict(new { error = "EmailAlreadyExists" });

        var employee = new Employee
        {
            FullName = request.FullName,
            Email = request.Email,
            FatherName = request.FatherName,
            Position = request.Position,
            BirthYear = request.BirthYear,
            LocationId = request.LocationId,
            Role = request.Role,
            PasswordHash = string.Empty,       // set by the employee at activation
            IsActive = true,
            ActivatedAtUtc = null,             // not activated yet
            InvitationExpiresUtc = DateTime.UtcNow.AddHours(_invitationOptions.ExpiryHours)
        };

        // The token embeds the (non-secret) employee id so activation can look the account up by
        // a key that survives activation; only the random part's hash is stored.
        var (activationToken, randomHash) = ActivationToken.Create(employee.Id);
        employee.InvitationTokenHash = randomHash;

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        // No email/SMS channel yet — return the PLAINTEXT token so it can be shared by hand.
        // (Base64Url is URL-safe, so it needs no additional encoding in the link.)
        return Ok(new
        {
            employeeId = employee.Id,
            activationToken,
            activationUrl = $"/activate?token={activationToken}"
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EmployeeUpdateRequest request)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        if (!await _db.Locations.AnyAsync(l => l.Id == request.LocationId))
            return BadRequest(new { error = "LocationNotFound" });

        if (await _db.Employees.AnyAsync(e => e.Email == request.Email && e.Id != id))
            return Conflict(new { error = "EmailAlreadyExists" });

        employee.FullName = request.FullName;
        employee.Email = request.Email;
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
    public async Task<IActionResult> Delete(Guid id)
    {
        Guid.TryParse(User.FindFirstValue("sub"), out var requesterId);
        if (id == requesterId)
            return BadRequest(new { error = "CannotDeleteSelf" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (employee is null)
            return NotFound(new { error = "EmployeeNotFound" });

        // Attendance/summary/device-change FKs are Restrict — refuse to delete an employee with
        // history (it would fail at the DB anyway). Deactivate such accounts instead.
        var hasHistory = await _db.AttendanceRecords.AnyAsync(a => a.EmployeeId == id)
                         || await _db.DailySummaries.AnyAsync(d => d.EmployeeId == id)
                         || await _db.DeviceChangeRequests.AnyAsync(r => r.EmployeeId == id || r.ReviewedByEmployeeId == id);
        if (hasHistory)
            return Conflict(new { error = "EmployeeHasHistory" });

        // DeviceBinding and ManagedLocations cascade; AuditLogs are set null.
        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = id });
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
}
