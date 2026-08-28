using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
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
[Authorize(Roles = "Admin")]
[Route("api/admin/device-bindings")]
public class AdminDeviceBindingController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminDeviceBindingController(AppDbContext db) => _db = db;

    // GET /api/admin/device-bindings — every active binding, newest first, so a freshly adopted
    // device is at the top where an admin will actually see it.
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await (
            from d in _db.DeviceBindings
            where d.IsActive
            join e in _db.Employees on d.EmployeeId equals e.Id
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

        var rows = await (
            from d in _db.DeviceBindings
            where d.IsActive
            join e in _db.Employees on d.EmployeeId equals e.Id
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

        var now = DateTime.UtcNow;
        foreach (var b in bindings)
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
        return Ok(new { revoked = bindings.Count });
    }

    // POST /api/admin/device-bindings/{id}/revoke — kill one context. It is NOT deleted: the row
    // survives with RevokedAtUtc set, which is what stops the next scan from silently re-adopting it.
    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var binding = await _db.DeviceBindings.FirstOrDefaultAsync(d => d.Id == id, HttpContext.RequestAborted);
        if (binding is null)
            return NotFound(new { error = "BindingNotFound" });

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
