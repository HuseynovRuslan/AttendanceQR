using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Api.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The bound devices themselves, as opposed to requests to change them. While AutoBind is on this is
/// the only place an unexpected device shows up — reviewing (and pruning) this list is the step that
/// makes the open adoption window safe to close later.
/// </summary>
[ApiController]
// Manager too. A device that will not scan is the complaint a branch manager gets first and can do
// nothing about, so revocation belonged with the person standing next to the poster. Every read is
// filtered to their branches and every revoke re-checks the employee — see ScopeAsync below.
[Authorize(Roles = "Admin,Manager")]
[Route("api/admin/device-bindings")]
public class AdminDeviceBindingController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminDeviceBindingController(AppDbContext db) => _db = db;

    /// <summary>The branches to filter reads by — null for an admin, meaning the whole company.</summary>
    private async Task<List<Guid>?> ManagedOrAllAsync()
        => User.Role() == EmployeeRole.Manager
            ? await LocationScopeRules.ManagedLocationIdsAsync(_db, User.EmployeeId(), HttpContext.RequestAborted)
            : null;

    /// <summary>May the caller act on this employee's device? Their branches and Role==Employee for a
    /// manager, everything for an admin — the same boundary as every other manager write, so revoking
    /// a device never becomes a way to reach a same-branch admin's account.</summary>
    private Task<bool> MayActOnAsync(Guid employeeId)
        => LocationScopeRules.CanManageEmployeeAsync(
            _db, User.EmployeeId(), User.Role(), employeeId, HttpContext.RequestAborted);

    // GET /api/admin/device-bindings — every active binding, newest first, so a freshly adopted
    // device is at the top where an admin will actually see it.
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var managed = await ManagedOrAllAsync();
        var rows = await (
            from d in _db.DeviceBindings
            where d.IsActive
            join e in _db.Employees on d.EmployeeId equals e.Id
            where managed == null || managed.Contains(e.LocationId)
            orderby d.BoundAtUtc descending
            select new
            {
                id = d.Id,
                employeeId = e.Id,
                employeeName = e.FullName,
                deviceLabel = d.DeviceLabel,
                deviceFingerprint = d.DeviceFingerprint,
                boundVia = d.BoundVia.ToString(),
                boundAtUtc = d.BoundAtUtc,
                lastSeenAtUtc = d.LastSeenAtUtc
            }).ToListAsync(HttpContext.RequestAborted);

        return Ok(rows);
    }

    /// <summary>
    /// Devices carrying MORE THAN ONE employee — the brigade phones.
    ///
    /// Until now the company could not see these at all. Every limit in the device rules is per
    /// employee (three devices each, three adoptions a month each), so nothing bounded the other axis
    /// and nothing displayed it: one handset could hold any number of accounts and no screen said so.
    /// That matters because a shared phone gives up the control the whole scheme rests on — one phone,
    /// one employee — for everybody on it. Giving it up can be the right call for a worker with no
    /// phone; not being able to SEE that it happened is never the right call.
    /// </summary>
    [HttpGet("shared")]
    public async Task<IActionResult> Shared()
    {
        var ct = HttpContext.RequestAborted;

        var managed = await ManagedOrAllAsync();
        var rows = await (
            from d in _db.DeviceBindings
            where d.IsActive
            join e in _db.Employees on d.EmployeeId equals e.Id
            // A manager sees the handsets their OWN people are on. A shared phone can straddle two
            // branches, so the count they see is the count they can act on — which is the honest one
            // for them, even though it may be smaller than the phone really carries.
            where managed == null || managed.Contains(e.LocationId)
            select new
            {
                d.DeviceFingerprint, d.DeviceLabel, d.BoundAtUtc, d.LastSeenAtUtc,
                bindingId = d.Id, employeeId = e.Id, e.FullName, e.Position, e.CanShareDevice,
            }).ToListAsync(ct);

        var devices = rows
            .GroupBy(r => r.DeviceFingerprint)
            .Where(g => g.Select(r => r.employeeId).Distinct().Count() > 1)
            // Busiest first: the phone carrying fifteen people is the one worth looking at.
            .OrderByDescending(g => g.Select(r => r.employeeId).Distinct().Count())
            .Select(g => new
            {
                fingerprint = g.Key,
                // The label comes from the user agent, so every binding on one handset agrees; taking
                // the first is not a guess.
                label = g.First().DeviceLabel,
                accountCount = g.Select(r => r.employeeId).Distinct().Count(),
                lastSeenAtUtc = g.Max(r => r.LastSeenAtUtc),
                employees = g
                    .OrderBy(r => r.FullName)
                    .Select(r => new
                    {
                        bindingId = r.bindingId,
                        employeeId = r.employeeId,
                        fullName = r.FullName,
                        position = r.Position,
                        // Somebody carried on a shared phone WITHOUT the permission is a leftover from
                        // before the rule existed. Not an error, but the row an admin should decide on.
                        canShareDevice = r.CanShareDevice,
                        boundAtUtc = r.BoundAtUtc,
                    })
                    .ToList(),
            })
            .ToList();

        return Ok(devices);
    }

    /// <summary>
    /// Revoke every account on one handset at once — the answer to a lost or stolen brigade phone.
    ///
    /// One at a time was the only way, which for a phone carrying fifteen people meant finding fifteen
    /// rows in a list ordered by something else entirely, at the moment somebody is telephoning to say
    /// the phone is gone.
    ///
    /// This does NOT sign anyone out. Bumping TokenVersion would, and mass sign-out is the one thing
    /// this product does not do: the people on a shared phone are the people who cannot re-enter a PIN.
    /// Revoking the bindings stops that handset scanning while every account stays exactly as it was.
    /// </summary>
    [HttpPost("device/revoke-all")]
    public async Task<IActionResult> RevokeDevice([FromQuery] string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return BadRequest(new { error = "FingerprintRequired" });

        var ct = HttpContext.RequestAborted;
        var bindings = await _db.DeviceBindings
            .Where(d => d.DeviceFingerprint == fingerprint && d.RevokedAtUtc == null)
            .ToListAsync(ct);

        if (bindings.Count == 0)
            return NotFound(new { error = "DeviceNotFound" });

        // A shared phone can straddle two branches. A manager detaches the accounts they are
        // responsible for and leaves the rest bound — silently revoking another branch's people
        // because their colleague's phone was lost would strand workers a different manager answers
        // for. An admin, whose scope is the company, detaches all of them.
        var actionable = new List<DeviceBinding>();
        foreach (var b in bindings)
            if (await MayActOnAsync(b.EmployeeId))
                actionable.Add(b);

        if (actionable.Count == 0)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        var now = DateTime.UtcNow;
        foreach (var b in actionable)
        {
            b.RevokedAtUtc = now;
            _db.AuditLogs.Add(new AuditLog
            {
                EmployeeId = b.EmployeeId,
                EventType = AuditEventType.DeviceBindingRevoked,
                Reason = $"{b.DeviceLabel} — ortaq cihaz, hamısı ləğv edildi",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
        }

        await _db.SaveChangesAsync(ct);
        // "skipped" so the screen can say the phone still carries somebody rather than implying the
        // handset is now clean.
        return Ok(new { revoked = actionable.Count, skipped = bindings.Count - actionable.Count });
    }

    // POST /api/admin/device-bindings/{id}/revoke — kill one context. It is NOT deleted: the row
    // survives with RevokedAtUtc set, which is what stops the next scan from silently re-adopting it.
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var binding = await _db.DeviceBindings.FirstOrDefaultAsync(d => d.Id == id, HttpContext.RequestAborted);
        if (binding is null)
            return NotFound(new { error = "BindingNotFound" });

        if (!await MayActOnAsync(binding.EmployeeId))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "OutOfScope" });

        if (!binding.IsActive)
            return Ok(new { status = "AlreadyRevoked" });

        binding.IsActive = false;
        binding.RevokedAtUtc = DateTime.UtcNow;

        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = binding.EmployeeId,
            EventType = AuditEventType.DeviceBindingRevoked,
            Reason = binding.DeviceLabel,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { status = "Revoked" });
    }
}
