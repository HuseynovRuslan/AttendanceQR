using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Admin-only management of who may hand tasks to whom. The admin picks a person (the assigner) and
/// sets exactly which employees that person is allowed to send tasks to. This is what keeps the
/// Tapşırıqlar section from applying to everyone — only admins, granted assigners and their recipients
/// ever see it (see <see cref="AdminTasksController.Access"/>).
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/task-permissions")]
public class AdminTaskPermissionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminTaskPermissionsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Every assigner that has been granted at least one recipient, with their recipient sets.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var ct = HttpContext.RequestAborted;
        var grants = await _db.TaskAssignPermissions.ToListAsync(ct);
        var names = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var givers = grants
            .GroupBy(g => g.AssignerEmployeeId)
            .Select(g => new
            {
                assignerId = g.Key,
                assignerName = names.GetValueOrDefault(g.Key, "—"),
                recipients = g.Select(r => new
                {
                    id = r.RecipientEmployeeId,
                    name = names.GetValueOrDefault(r.RecipientEmployeeId, "—"),
                }).OrderBy(r => r.name).ToList(),
            })
            .OrderBy(x => x.assignerName)
            .ToList();

        return Ok(givers);
    }

    /// <summary>Replace the full recipient set for one assigner. Empty list removes their giver status.</summary>
    [HttpPut("{assignerId:guid}")]
    public async Task<IActionResult> SetRecipients(Guid assignerId, [FromBody] SetTaskRecipientsRequest request)
    {
        var ct = HttpContext.RequestAborted;

        if (!await _db.Employees.AnyAsync(e => e.Id == assignerId, ct))
            return BadRequest(new { error = "EmployeeNotFound" });

        // Distinct, non-self, existing, active recipients only.
        var wanted = (request.RecipientEmployeeIds ?? Array.Empty<Guid>())
            .Where(id => id != assignerId)
            .Distinct()
            .ToArray();

        var validIds = await _db.Employees
            .Where(e => wanted.Contains(e.Id) && e.IsActive)
            .Select(e => e.Id)
            .ToListAsync(ct);

        var existing = await _db.TaskAssignPermissions
            .Where(p => p.AssignerEmployeeId == assignerId)
            .ToListAsync(ct);

        var toRemove = existing.Where(p => !validIds.Contains(p.RecipientEmployeeId)).ToList();
        var existingIds = existing.Select(p => p.RecipientEmployeeId).ToHashSet();
        var toAdd = validIds.Where(id => !existingIds.Contains(id))
            .Select(id => new TaskAssignPermission { AssignerEmployeeId = assignerId, RecipientEmployeeId = id })
            .ToList();

        if (toRemove.Count > 0) _db.TaskAssignPermissions.RemoveRange(toRemove);
        if (toAdd.Count > 0) _db.TaskAssignPermissions.AddRange(toAdd);
        if (toRemove.Count > 0 || toAdd.Count > 0) await _db.SaveChangesAsync(ct);

        return Ok(new { assignerId, recipientCount = validIds.Count });
    }
}
