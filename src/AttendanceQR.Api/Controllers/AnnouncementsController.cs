using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Employee-facing read of the tenant's active announcements — shown as a home banner and in the
/// notifications feed. Any authenticated user; tenant-scoped by the DbContext filter.
/// </summary>
[ApiController]
[Authorize]
[Route("api/announcements")]
public class AnnouncementsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AnnouncementsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Active()
    {
        var rows = await _db.Announcements
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { id = a.Id, message = a.Message, createdAtUtc = a.CreatedAtUtc })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }
}

/// <summary>Admin management of announcements — post one, list all (active + retired), retire one.</summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/announcements")]
public class AdminAnnouncementsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminAnnouncementsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.Announcements
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new { id = a.Id, message = a.Message, isActive = a.IsActive, createdAtUtc = a.CreatedAtUtc })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AnnouncementRequest request)
    {
        var message = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { error = "MessageRequired" });
        if (message.Length > 1000)
            return BadRequest(new { error = "MessageTooLong" });

        var announcement = new Announcement { Message = message };
        _db.Announcements.Add(announcement);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { id = announcement.Id, message = announcement.Message, isActive = true, createdAtUtc = announcement.CreatedAtUtc });
    }

    // Retire (soft-delete) so it disappears for employees without vanishing from the admin's history.
    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id)
    {
        var announcement = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id, HttpContext.RequestAborted);
        if (announcement is null)
            return NotFound(new { error = "AnnouncementNotFound" });
        announcement.IsActive = false;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { id, isActive = false });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var announcement = await _db.Announcements.FirstOrDefaultAsync(a => a.Id == id, HttpContext.RequestAborted);
        if (announcement is null)
            return NotFound(new { error = "AnnouncementNotFound" });
        _db.Announcements.Remove(announcement);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { deleted = id });
    }
}
