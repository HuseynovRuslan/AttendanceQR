using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The company's list of job titles. Employees pick from it instead of typing, which is the only
/// reliable way to stop one job existing under three spellings.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/positions")]
public class AdminPositionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminPositionsController(AppDbContext db) => _db = db;

    /// <summary>The catalogue with headcounts — the count is what tells an admin which of two similar
    /// titles is the real one and which is a typo to merge away.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var ct = HttpContext.RequestAborted;
        var positions = await _db.JobPositions.OrderBy(p => p.Name).ToListAsync(ct);
        var counts = await _db.Employees
            .Where(e => e.IsActive && e.Position != null && e.Position != "")
            .GroupBy(e => e.Position!)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byName = counts.ToDictionary(c => c.Name, c => c.Count);

        // Titles held by someone but missing from the catalogue — bulk import can create them, and
        // hiding them would make the list look complete while employees sit under names it doesn't
        // contain. They are listed as unregistered so they can be merged or adopted.
        var orphans = counts
            .Where(c => positions.All(p => p.Name != c.Name))
            .Select(c => new { id = (Guid?)null, name = c.Name, count = c.Count, inCatalogue = false });

        return Ok(positions
            .Select(p => new { id = (Guid?)p.Id, name = p.Name, count = byName.GetValueOrDefault(p.Name, 0), inCatalogue = true })
            .Concat(orphans)
            .OrderBy(x => x.name));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PositionRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "NameRequired" });
        if (await _db.JobPositions.AnyAsync(p => p.Name == name, ct))
            return Conflict(new { error = "PositionExists" });

        var position = new JobPosition { Name = name };
        _db.JobPositions.Add(position);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = position.Id, name = position.Name, count = 0, inCatalogue = true });
    }

    /// <summary>
    /// Renames a title, or merges it into another when the new name already exists.
    ///
    /// Merging is the whole reason this endpoint rewrites employees: the duplicates already in the
    /// data ("Layihə Rəhəri" vs "Layihə rəhbəri") can only be fixed by moving people onto one name.
    /// Ballot exclusions are rewritten too, otherwise a campaign would go on barring a title that no
    /// longer exists and quietly stop excluding anyone.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] PositionRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "NameRequired" });

        var position = await _db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (position is null) return NotFound(new { error = "PositionNotFound" });
        var oldName = position.Name;
        if (oldName == name) return Ok(new { id = position.Id, name, merged = false });

        var target = await _db.JobPositions.FirstOrDefaultAsync(p => p.Name == name, ct);
        var moved = await RepointAsync(oldName, name, ct);

        if (target is null)
            position.Name = name;
        else
            _db.JobPositions.Remove(position); // merged into the existing title

        await _db.SaveChangesAsync(ct);
        return Ok(new { id = target?.Id ?? position.Id, name, merged = target is not null, movedEmployees = moved });
    }

    /// <summary>Adopts a title that people already hold but the catalogue never had.</summary>
    [HttpPost("adopt")]
    public async Task<IActionResult> Adopt([FromBody] PositionRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "NameRequired" });
        if (await _db.JobPositions.AnyAsync(p => p.Name == name, ct))
            return Conflict(new { error = "PositionExists" });

        var position = new JobPosition { Name = name };
        _db.JobPositions.Add(position);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = position.Id, name });
    }

    /// <summary>Merges an unregistered or duplicate title into another, then removes it.</summary>
    [HttpPost("merge")]
    public async Task<IActionResult> Merge([FromBody] PositionMergeRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var from = request.From.Trim();
        var into = request.Into.Trim();
        if (from.Length == 0 || into.Length == 0) return BadRequest(new { error = "NameRequired" });
        if (from == into) return BadRequest(new { error = "SameName" });
        if (!await _db.JobPositions.AnyAsync(p => p.Name == into, ct))
            return BadRequest(new { error = "TargetNotInCatalogue" });

        var moved = await RepointAsync(from, into, ct);
        var source = await _db.JobPositions.FirstOrDefaultAsync(p => p.Name == from, ct);
        if (source is not null) _db.JobPositions.Remove(source);
        await _db.SaveChangesAsync(ct);

        return Ok(new { movedEmployees = moved, into });
    }

    /// <summary>Removes a title nobody holds. One still in use must be merged instead, so no employee
    /// is left pointing at a title the company no longer recognises.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var position = await _db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (position is null) return NotFound(new { error = "PositionNotFound" });

        var inUse = await _db.Employees.CountAsync(e => e.IsActive && e.Position == position.Name, ct);
        if (inUse > 0) return BadRequest(new { error = "PositionInUse", employees = inUse });

        _db.JobPositions.Remove(position);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = id });
    }

    /// <summary>Moves everyone on one title to another and fixes ballot exclusions that named it.</summary>
    private async Task<int> RepointAsync(string from, string into, CancellationToken ct)
    {
        var employees = await _db.Employees.Where(e => e.Position == from).ToListAsync(ct);
        foreach (var e in employees) e.Position = into;

        var campaigns = await _db.VoteCampaigns.ToListAsync(ct);
        foreach (var c in campaigns.Where(c => c.ExcludedPositions.Contains(from)))
            c.ExcludedPositions = c.ExcludedPositions.Select(p => p == from ? into : p).Distinct().ToList();

        return employees.Count;
    }
}
