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
