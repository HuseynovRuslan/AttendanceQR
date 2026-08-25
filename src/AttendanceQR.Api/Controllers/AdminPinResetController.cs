using System.Security.Cryptography;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin side of the "PIN-i unutdum" flow: the queue of employees who asked for a reset, and the two
/// actions on each — resolve (reset the PIN, get a temporary one to pass on) or dismiss.
///
/// A branch manager works this queue too, for their own staff only. They can already reset those same
/// people's PINs from their own screen (ManagerController), so keeping them out of the queue did not
/// protect anything — it just meant the employee who forgot their PIN waited for an admin who was not
/// in the building. Every row and every action runs through CanManageEmployeeAsync, which stops at
/// Role==Employee inside their ManagedLocations: the branch-only reach that let a manager reset a
/// same-branch ADMIN's PIN was the P0 of 2026-08-08 and does not come back here.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/pin-resets")]
public class AdminPinResetController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginLockoutStore _lockout;

    public AdminPinResetController(AppDbContext db, IPasswordHasher passwordHasher, ILoginLockoutStore lockout)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _lockout = lockout;
    }

    // GET /api/admin/pin-resets — the pending queue, oldest first, with enough to identify the person.
    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var ct = HttpContext.RequestAborted;
        var people = _db.Employees.AsQueryable();

        // Manager scope: the queue shows only requests filed by staff this caller may act on, because
        // every row here is an offer to mint a credential for that person.
        if (User.Role() == EmployeeRole.Manager)
        {
            var managed = await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), ct);
            people = people.Where(e => managed.Contains(e.LocationId) && e.Role == EmployeeRole.Employee);
        }

        var rows = await _db.PinResetRequests
            .Where(r => r.Status == PinResetStatus.Pending)
            .OrderBy(r => r.RequestedAtUtc)
            .Join(people, r => r.EmployeeId, e => e.Id, (r, e) => new
            {
                requestId = r.Id,
                employeeId = e.Id,
                employeeName = e.FullName,
                phoneNumber = e.PhoneNumber,
                email = e.Email,
                requestedAtUtc = r.RequestedAtUtc
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // POST /api/admin/pin-resets/{id}/resolve — reset the employee's PIN and close the request. Returns
    // the plaintext temporary PIN so the admin can pass it on (a hashed PIN can never be read back).
    // Same reset behavior as AdminController.ResetPin: force a change on next login, kill live tokens,
    // clear the lockout.
    [HttpPost("{id:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var employeeId = await _db.PinResetRequests
            .Where(r => r.Id == id)
            .Select(r => (Guid?)r.EmployeeId)
            .FirstOrDefaultAsync(ct);
        if (employeeId is null)
            return NotFound(new { error = "RequestNotFound" });

        // The same boundary the list applies, re-checked on the action — a request id guessed or held
        // from before a branch reassignment must not resolve into a PIN. Checked before the atomic
        // claim below so a refusal does not consume somebody else's queue entry.
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), employeeId.Value, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        // An impersonation session must not mint a credential for a privileged account: this endpoint
        // returns the plaintext PIN, so resolving a request filed against the borrowed admin (or any
        // other admin) is the same takeover AdminController.ResetPin refuses. Checked BEFORE the claim
        // below, so a refusal does not consume the customer's queue entry.
        if (User.IsImpersonating())
        {
            var target = await _db.Employees
                .Where(e => e.Id == employeeId.Value)
                .Select(e => new { e.Id, e.Role })
                .FirstOrDefaultAsync(ct);
            if (target is not null && (target.Id == User.EmployeeId() || target.Role == EmployeeRole.Admin))
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "NotDuringImpersonation" });
        }

        // Atomically CLAIM the request (Pending -> Resolved) in one UPDATE. Without this, two admins
        // resolving the same id at once would each pass a read-then-check guard and each reset the PIN
        // to a DIFFERENT random value — last write wins, and one admin reads out a PIN that no longer
        // works. Only the caller whose UPDATE matched a still-Pending row proceeds to the reset.
        var claimed = await _db.PinResetRequests
            .Where(r => r.Id == id && r.Status == PinResetStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PinResetStatus.Resolved)
                .SetProperty(r => r.ResolvedByEmployeeId, (Guid?)User.EmployeeId())
                .SetProperty(r => r.ResolvedAtUtc, (DateTime?)DateTime.UtcNow), ct);
        if (claimed == 0)
            return Conflict(new { error = "AlreadyReviewed" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId.Value, ct);
        if (employee is null || employee.ActivatedAtUtc is null)
            return Conflict(new { error = "NotActivated" });

        var pin = PinRules.Generate();
        employee.PasswordHash = _passwordHasher.Hash(pin);
        employee.MustChangePin = true;   // the employee picks their own PIN on next login
        employee.TokenVersion++;         // kill any session still holding the old PIN's token

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = employee.Id,
            EventType = AuditEventType.PinResetResolved,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync(ct);

        // Clear the lockout for both identifiers, same key Login would have locked on.
        var tenantId = _db.CurrentTenantId;
        _lockout.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.Email));
        if (employee.PhoneNumber is not null)
            _lockout.RecordSuccess(LoginIdentity.LockoutKey(tenantId, employee.PhoneNumber));

        return Ok(new { tempPin = pin });
    }

    // POST /api/admin/pin-resets/{id}/dismiss — close a bogus or already-handled request without
    // touching the account.
    [HttpPost("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id)
    {
        var ct = HttpContext.RequestAborted;

        // Same boundary as Resolve. Dismissing hands out no credential, but it silences somebody
        // else's request — a locked-out employee whose queue entry vanishes has no way to tell it
        // ever arrived, and files another.
        var owner = await _db.PinResetRequests.Where(r => r.Id == id)
            .Select(r => (Guid?)r.EmployeeId).FirstOrDefaultAsync(ct);
        if (owner is null)
            return NotFound(new { error = "RequestNotFound" });
        if (!await LocationScopeRules.CanManageEmployeeAsync(_db, User.EmployeeId(), User.Role(), owner.Value, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        // Atomic claim, same as Resolve — flip only a still-Pending row so concurrent dismisses (or a
        // dismiss racing a resolve) can't both act.
        var claimed = await _db.PinResetRequests
            .Where(r => r.Id == id && r.Status == PinResetStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, PinResetStatus.Dismissed)
                .SetProperty(r => r.ResolvedByEmployeeId, (Guid?)User.EmployeeId())
                .SetProperty(r => r.ResolvedAtUtc, (DateTime?)DateTime.UtcNow), ct);
        if (claimed == 0)
        {
            var exists = await _db.PinResetRequests.AnyAsync(r => r.Id == id, ct);
            return exists ? Conflict(new { error = "AlreadyReviewed" }) : NotFound(new { error = "RequestNotFound" });
        }

        return Ok(new { status = "Dismissed" });
    }
}
