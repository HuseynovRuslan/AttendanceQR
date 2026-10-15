using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// Structures — the optional layer between the company and its branches («Aeroport yolu» over the
/// branches along that road). See docs/strukturlar-ve-selahiyyetler.md.
///
/// Admin-only for now. Who may manage structures becomes a permission (structures.manage) once the
/// permission model lands; until then it is the company's admin, the same as creating a branch.
///
/// Nothing here touches what a branch IS — its fence, its poster, its shifts, its people or their days.
/// A structure is a label over branches: archiving one needs it empty, and moving a branch rewrites one
/// column and an audit row.
/// </summary>
[Permission(AccessPermissions.StructuresManage)]
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/structures")]
public class StructuresController : ControllerBase
{
    private const int MaxNameLength = 120;
    private const int MaxAssign = 500;

    private readonly AppDbContext _db;

    public StructuresController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/admin/structures — every structure with its branches, and the branches in none. Archived
    // structures are listed last and marked, so a name that was retired is still recognisable.
    [Permission(AccessPermissions.StructuresView)]
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var ct = HttpContext.RequestAborted;
        var structures = await _db.Structures.AsNoTracking()
            .OrderBy(s => s.ArchivedAtUtc != null).ThenBy(s => s.Name)
            .ToListAsync(ct);
        var branches = await _db.Locations.AsNoTracking()
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.IsActive, l.StructureId })
            .ToListAsync(ct);

        return Ok(new
        {
            structures = structures.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                archived = s.ArchivedAtUtc != null,
                createdAtUtc = s.CreatedAtUtc,
                locations = branches.Where(b => b.StructureId == s.Id)
                    .Select(b => new { id = b.Id, name = b.Name, isActive = b.IsActive }),
            }),
            unassigned = branches.Where(b => b.StructureId == null)
                .Select(b => new { id = b.Id, name = b.Name, isActive = b.IsActive }),
        });
    }

    // POST /api/admin/structures
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] StructureRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var name = request.Name?.Trim() ?? string.Empty;
        if (NameProblem(name) is { } problem)
            return BadRequest(new { error = problem });
        if (await NameTakenAsync(name, exceptId: null, ct))
            return Conflict(new { error = "NameTaken" });

        var structure = new Structure { Name = name };
        _db.Structures.Add(structure);
        Audit(AuditEventType.StructureChanged, $"Struktur yaradıldı: {name}");
        return await SaveAsync(ct) ?? Ok(new { id = structure.Id, name = structure.Name, archived = false });
    }

    // PUT /api/admin/structures/{id} — rename.
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] StructureRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var structure = await _db.Structures.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (structure is null)
            return NotFound(new { error = "StructureNotFound" });

        var name = request.Name?.Trim() ?? string.Empty;
        if (NameProblem(name) is { } problem)
            return BadRequest(new { error = problem });
        if (name == structure.Name)
            return Ok(new { id = structure.Id, name = structure.Name, archived = structure.ArchivedAtUtc != null });
        // Only a live structure holds its name (the unique index is filtered the same way).
        if (structure.ArchivedAtUtc is null && await NameTakenAsync(name, exceptId: id, ct))
            return Conflict(new { error = "NameTaken" });

        Audit(AuditEventType.StructureChanged, $"Struktur adı dəyişdi: {structure.Name} → {name}");
        structure.Name = name;
        return await SaveAsync(ct) ?? Ok(new { id = structure.Id, name = structure.Name, archived = structure.ArchivedAtUtc != null });
    }

    // POST /api/admin/structures/{id}/archive — only an empty structure. Its branches are moved first, on
    // purpose: archiving must never leave a branch pointing at something nobody can see or reach.
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var structure = await _db.Structures.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (structure is null)
            return NotFound(new { error = "StructureNotFound" });
        if (structure.ArchivedAtUtc is not null)
            return Ok(new { id, archived = true });

        var inside = await _db.Locations.CountAsync(l => l.StructureId == id, ct);
        if (inside > 0)
            return Conflict(new { error = "StructureNotEmpty", locations = inside });

        structure.ArchivedAtUtc = DateTime.UtcNow;
        Audit(AuditEventType.StructureChanged, $"Struktur arxivləndi: {structure.Name}");
        return await SaveAsync(ct) ?? Ok(new { id, archived = true });
    }

    // POST /api/admin/structures/{id}/restore
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var structure = await _db.Structures.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (structure is null)
            return NotFound(new { error = "StructureNotFound" });
        if (structure.ArchivedAtUtc is null)
            return Ok(new { id, archived = false });
        if (await NameTakenAsync(structure.Name, exceptId: id, ct))
            return Conflict(new { error = "NameTaken" });

        structure.ArchivedAtUtc = null;
        Audit(AuditEventType.StructureChanged, $"Struktur bərpa edildi: {structure.Name}");
        return await SaveAsync(ct) ?? Ok(new { id, archived = false });
    }

    // PUT /api/admin/structures/assign — put branches into a structure (or, with a null structure, take them
    // out of any). Both the structure and every branch are looked up through the company's own query
    // filter, so another company's id simply is not found; the composite foreign key would refuse it anyway.
    [HttpPut("assign")]
    public async Task<IActionResult> Assign([FromBody] AssignStructureRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var ids = (request.LocationIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest(new { error = "NoLocations" });
        if (ids.Count > MaxAssign)
            return BadRequest(new { error = "TooManyLocations", max = MaxAssign });

        Structure? target = null;
        if (request.StructureId is Guid structureId)
        {
            target = await _db.Structures.FirstOrDefaultAsync(s => s.Id == structureId, ct);
            if (target is null)
                return NotFound(new { error = "StructureNotFound" });
            if (target.ArchivedAtUtc is not null)
                return Conflict(new { error = "StructureArchived" });
        }

        var locations = await _db.Locations.Where(l => ids.Contains(l.Id)).ToListAsync(ct);
        if (locations.Count != ids.Count)
            return NotFound(new { error = "LocationNotFound" });

        var names = await _db.Structures.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var moved = 0;
        foreach (var location in locations.Where(l => l.StructureId != request.StructureId))
        {
            var from = location.StructureId is Guid f && names.TryGetValue(f, out var fromName) ? fromName : "struktursuz";
            Audit(AuditEventType.LocationStructureChanged, $"{location.Name}: {from} → {target?.Name ?? "struktursuz"}");
            location.StructureId = request.StructureId;
            moved++;
        }

        if (moved > 0 && await SaveAsync(ct) is { } failed)
            return failed;
        return Ok(new { moved });
    }

    private static string? NameProblem(string name) =>
        name.Length == 0 ? "NameRequired" : name.Length > MaxNameLength ? "NameTooLong" : null;

    private Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lowered = name.ToLower();
        return _db.Structures.AnyAsync(s =>
            s.ArchivedAtUtc == null && s.Id != exceptId && s.Name.ToLower() == lowered, ct);
    }

    private void Audit(AuditEventType type, string reason) =>
        _db.AuditLogs.Add(new AuditLog
        {
            EmployeeId = User.EmployeeId(),
            EventType = type,
            Reason = reason,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });

    // Two admins naming two structures alike at the same moment both pass NameTakenAsync; the filtered
    // unique index lets one through and refuses the other, which reads as the same 409.
    private async Task<IActionResult?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
                                           { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "NameTaken" });
        }
    }
}
