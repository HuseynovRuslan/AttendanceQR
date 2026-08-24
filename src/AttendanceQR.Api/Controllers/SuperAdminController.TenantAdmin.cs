using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Handing a finished company to the person who will run it.
///
/// The operator builds a company before it has an owner: branches, staff, shifts, the calendar. Until
/// now the customer's admin had to be named in the very first field of the very first form, before any
/// of that existed — and it was one-shot, because an impersonation session is refused on every route
/// that creates, promotes or re-credentials an Admin (AdminController: invite, bulk rows, promotion,
/// reinvite, reset-pin). Get the number wrong, or have the customer change who holds the account
/// halfway through the build, and there was no way back from inside the company.
///
/// So the naming moves to the end, and to the operator's own surface, where it is not impersonating
/// anybody: this endpoint gives the company's admin account to a real person and issues the temporary
/// PIN they will sign in with. It is also the repair for a tenant that has lost every admin — the
/// console could reset and reactivate people, but it could not create one, so a company that went
/// headless could not be recovered without SQL.
///
/// It does NOT plant an extra account. A company is created with exactly one admin, and if that admin
/// was never given a phone (the ordinary case now) this claims it rather than adding a second.
/// </summary>
public partial class SuperAdminController
{
    // POST /api/super/tenants/{id}/admin — name the customer's admin and issue their temporary PIN.
    [HttpPost("tenants/{id:guid}/admin")]
    public async Task<IActionResult> SetTenantAdmin(Guid id, [FromBody] SetTenantAdminRequest request)
    {
        if (!await CanAsync(OperatorPermission.ManageTenants, HttpContext.RequestAborted))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Forbidden" });

        var ct = HttpContext.RequestAborted;
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null)
            return NotFound(new { error = "TenantNotFound" });

        var phone = PhoneNumbers.Normalize(request.Phone);
        if (phone is null)
            return BadRequest(new { error = "AdminPhoneInvalid" });

        var pin = string.IsNullOrWhiteSpace(request.Pin) ? PinRules.Generate() : request.Pin.Trim();
        if (!PinRules.IsWellFormed(pin))
            return BadRequest(new { error = "AdminPinInvalid" });
        if (PinRules.IsTooWeak(pin))
            return BadRequest(new { error = "AdminPinTooWeak" });

        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? "Admin" : request.FullName.Trim();

        var result = await WithTenantAsync(tenant.Id, async () =>
        {
            // Inside the customer's scope the query filter does the tenant check for us, so this asks
            // "is anyone in THIS company already on that number" — the same question the tenant's own
            // admin screen asks, and the reason it must not be asked before the scope moves.
            if (await _db.Employees.AnyAsync(e => e.PhoneNumber == phone, ct))
                return (Conflict: true, Employee: (Employee?)null, Created: false);

            // The account the company was created with, if nobody has claimed it. "Unclaimed" is
            // exactly what it says: an admin with no phone and no email cannot be signed in as, so it
            // belongs to nobody and can be given away. An admin that already has either is a real
            // person — this endpoint adds a colleague beside them rather than taking their account.
            var unclaimed = await _db.Employees
                .Where(e => e.Role == EmployeeRole.Admin && e.IsActive && e.PhoneNumber == null && e.Email == null)
                .OrderBy(e => e.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (unclaimed is not null)
            {
                unclaimed.FullName = fullName;
                unclaimed.PhoneNumber = phone;
                unclaimed.PasswordHash = _passwordHasher.Hash(pin);
                unclaimed.MustChangePin = true;
                // Nothing can be holding a session for an account nobody could sign in as, but the
                // bump costs a byte and means the same code is safe if this is ever pointed at one.
                unclaimed.TokenVersion++;
                await _db.SaveChangesAsync(ct);
                return (Conflict: false, Employee: unclaimed, Created: false);
            }

            // Location has no created-at column, so "the first branch" is the starter one by name
            // where it still exists, and otherwise simply an active one — this only decides which
            // branch the admin's row is filed under, and they can move themselves.
            var location = await _db.Locations
                .Where(l => l.IsActive)
                .OrderBy(l => l.Name == "Baş ofis" ? 0 : 1)
                .ThenBy(l => l.Name)
                .FirstOrDefaultAsync(ct);
            if (location is null)
                return (Conflict: false, Employee: (Employee?)null, Created: false);

            var admin = new Employee
            {
                FullName = fullName,
                Email = null,
                PhoneNumber = phone,
                Role = EmployeeRole.Admin,
                LocationId = location.Id,
                PasswordHash = _passwordHasher.Hash(pin),
                IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow,
                MustChangePin = true,
            };
            _db.Employees.Add(admin);
            await _db.SaveChangesAsync(ct);
            return (Conflict: false, Employee: admin, Created: true);
        });

        if (result.Conflict)
            return Conflict(new { error = "PhoneAlreadyExists" });
        if (result.Employee is null)
            return Conflict(new { error = "NoLocation" });

        await AuditAsync(
            result.Created ? "TenantAdminCreated" : "TenantAdminClaimed",
            tenant.Id, tenant.Slug, $"{fullName} ({phone})", ct);

        return Ok(new
        {
            id = result.Employee.Id,
            fullName,
            phone,
            created = result.Created,
            // Shown once, like every other temporary PIN on this surface.
            tempPin = pin,
        });
    }
}
