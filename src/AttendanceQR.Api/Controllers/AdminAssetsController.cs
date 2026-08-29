using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The company's computer equipment and who is responsible for each piece.
///
/// Admin only. A branch manager can see who is at work today, but the equipment register is a
/// company-wide asset list — it carries purchase prices, and half of it sits at branches they do not
/// manage, so scoping it per branch would show a manager a list that is neither complete nor theirs.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/assets")]
public class AdminAssetsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminAssetsController(AppDbContext db) => _db = db;

    /// <summary>
    /// The register, newest first, with the holder's and branch's names resolved.
    ///
    /// Filtering happens here rather than in the browser because the answer to "where is inventory
    /// number 000431" has to be right for a company with two thousand rows, not just for one whose
    /// list happens to fit on a page.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] Guid? employeeId)
    {
        var ct = HttpContext.RequestAborted;
        var query = _db.Assets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.InventoryNumber, pattern)
                || EF.Functions.ILike(a.Name, pattern)
                || (a.SerialNumber != null && EF.Functions.ILike(a.SerialNumber, pattern))
                || (a.Brand != null && EF.Functions.ILike(a.Brand, pattern))
                || (a.Model != null && EF.Functions.ILike(a.Model, pattern)));
        }

        if (TryParseType(type, out var parsedType)) query = query.Where(a => a.Type == parsedType);
        if (TryParseStatus(status, out var parsedStatus)) query = query.Where(a => a.Status == parsedStatus);
        if (employeeId is { } holder) query = query.Where(a => a.AssignedEmployeeId == holder);

        var assets = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(ct);

        return Ok(await ToDtosAsync(assets, ct));
    }

    /// <summary>Headline counts for the stat cards — the same numbers an admin would otherwise get by
    /// filtering the list four times.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var ct = HttpContext.RequestAborted;
        var byStatus = await _db.Assets
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(AssetStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        return Ok(new
        {
            total = byStatus.Sum(x => x.Count),
            inStock = Count(AssetStatus.InStock),
            assigned = Count(AssetStatus.Assigned),
            inRepair = Count(AssetStatus.InRepair),
            writtenOff = Count(AssetStatus.WrittenOff),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AssetRequest request)
    {
        var ct = HttpContext.RequestAborted;

        var inventoryNumber = (request.InventoryNumber ?? string.Empty).Trim();
        var name = (request.Name ?? string.Empty).Trim();
        if (inventoryNumber.Length == 0) return BadRequest(new { error = "InventoryNumberRequired" });
        if (name.Length == 0) return BadRequest(new { error = "NameRequired" });
        if (!TryParseType(request.Type, out var type)) return BadRequest(new { error = "InvalidType" });
        if (await _db.Assets.AnyAsync(a => a.InventoryNumber == inventoryNumber, ct))
            return Conflict(new { error = "InventoryNumberExists" });
        if (!await LocationExistsAsync(request.LocationId, ct))
            return BadRequest(new { error = "LocationNotFound" });

        // A brand-new card names nobody, so "assigned" is not one of the states it can be created in —
        // it would claim a holder the row does not have. Assigning is its own act.
        var status = TryParseStatus(request.Status, out var s) && s is AssetStatus.InRepair or AssetStatus.WrittenOff
            ? s
            : AssetStatus.InStock;

        var asset = new Asset
        {
            InventoryNumber = inventoryNumber,
            Name = name,
            Type = type,
            Status = status,
            Brand = Trimmed(request.Brand),
            Model = Trimmed(request.Model),
            SerialNumber = Trimmed(request.SerialNumber),
            PurchaseDate = ParseDateOrNull(request.PurchaseDate),
            PurchasePrice = request.PurchasePrice,
            LocationId = request.LocationId,
            Notes = Trimmed(request.Notes),
        };

        _db.Assets.Add(asset);
        await _db.SaveChangesAsync(ct);
        return Ok((await ToDtosAsync(new[] { asset }, ct)).Single());
    }

    /// <summary>
    /// Edits the card. Moving the status away from "assigned" returns the device to stock, because the
    /// alternative — a row marked "in repair" that still names a holder — is the one state that makes
    /// the register lie about who has what.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AssetRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound(new { error = "AssetNotFound" });

        var inventoryNumber = (request.InventoryNumber ?? string.Empty).Trim();
        var name = (request.Name ?? string.Empty).Trim();
        if (inventoryNumber.Length == 0) return BadRequest(new { error = "InventoryNumberRequired" });
        if (name.Length == 0) return BadRequest(new { error = "NameRequired" });
        if (!TryParseType(request.Type, out var type)) return BadRequest(new { error = "InvalidType" });
        if (await _db.Assets.AnyAsync(a => a.Id != id && a.InventoryNumber == inventoryNumber, ct))
            return Conflict(new { error = "InventoryNumberExists" });
        if (!await LocationExistsAsync(request.LocationId, ct))
            return BadRequest(new { error = "LocationNotFound" });

        if (TryParseStatus(request.Status, out var status))
        {
            // "Assigned" is reached by assigning, not by picking it from a dropdown — there is no
            // employee in this payload who could be responsible for the device.
            if (status == AssetStatus.Assigned && asset.AssignedEmployeeId is null)
                return BadRequest(new { error = "AssignInstead" });

            if (status != AssetStatus.Assigned && asset.AssignedEmployeeId is not null)
            {
                asset.AssignedEmployeeId = null;
                asset.AssignedAtUtc = null;
            }

            asset.Status = status;
        }

        asset.InventoryNumber = inventoryNumber;
        asset.Name = name;
        asset.Type = type;
        asset.Brand = Trimmed(request.Brand);
        asset.Model = Trimmed(request.Model);
        asset.SerialNumber = Trimmed(request.SerialNumber);
        asset.PurchaseDate = ParseDateOrNull(request.PurchaseDate);
        asset.PurchasePrice = request.PurchasePrice;
        asset.LocationId = request.LocationId;
        asset.Notes = Trimmed(request.Notes);

        await _db.SaveChangesAsync(ct);
        return Ok((await ToDtosAsync(new[] { asset }, ct)).Single());
    }

    /// <summary>
    /// Hands the equipment to an employee. Reassigning straight from one holder to another is allowed:
    /// that is what actually happens when someone leaves and their laptop goes to their replacement,
    /// and demanding a return step first only invites the return being skipped.
    /// </summary>
    [HttpPost("{id:guid}/assign")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssetAssignRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound(new { error = "AssetNotFound" });
        if (asset.Status == AssetStatus.WrittenOff) return BadRequest(new { error = "AssetWrittenOff" });

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null) return NotFound(new { error = "EmployeeNotFound" });
        if (!employee.IsActive) return BadRequest(new { error = "EmployeeInactive" });

        asset.AssignedEmployeeId = employee.Id;
        asset.AssignedAtUtc = DateTime.UtcNow;
        asset.Status = AssetStatus.Assigned;
        if (Trimmed(request.Notes) is { } note) asset.Notes = note;

        await _db.SaveChangesAsync(ct);
        return Ok((await ToDtosAsync(new[] { asset }, ct)).Single());
    }

    /// <summary>Takes the equipment back into stock. Answers "the employee gave it back" — whether it
    /// then goes off for repair is a separate edit.</summary>
    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound(new { error = "AssetNotFound" });
        if (asset.AssignedEmployeeId is null) return BadRequest(new { error = "AssetNotAssigned" });

        asset.AssignedEmployeeId = null;
        asset.AssignedAtUtc = null;
        asset.Status = AssetStatus.InStock;

        await _db.SaveChangesAsync(ct);
        return Ok((await ToDtosAsync(new[] { asset }, ct)).Single());
    }

    /// <summary>Removes a card entirely — for one typed in by mistake. Equipment the company is done
    /// with is written off instead, which keeps it in the register.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var asset = await _db.Assets.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return NotFound(new { error = "AssetNotFound" });
        if (asset.AssignedEmployeeId is not null) return BadRequest(new { error = "AssetAssigned" });

        _db.Assets.Remove(asset);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = id });
    }

    // --- helpers -------------------------------------------------------------

    /// <summary>Resolves holder and branch names in two queries rather than one per row.</summary>
    private async Task<List<object>> ToDtosAsync(IReadOnlyCollection<Asset> assets, CancellationToken ct)
    {
        var employeeIds = assets.Where(a => a.AssignedEmployeeId is not null)
            .Select(a => a.AssignedEmployeeId!.Value).Distinct().ToList();
        var locationIds = assets.Where(a => a.LocationId is not null)
            .Select(a => a.LocationId!.Value).Distinct().ToList();

        var employees = employeeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Employees.Where(e => employeeIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var locations = locationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Locations.Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        return assets.Select(a => (object)new
        {
            id = a.Id,
            inventoryNumber = a.InventoryNumber,
            type = a.Type.ToString(),
            name = a.Name,
            brand = a.Brand,
            model = a.Model,
            serialNumber = a.SerialNumber,
            purchaseDate = a.PurchaseDate?.ToString("yyyy-MM-dd"),
            purchasePrice = a.PurchasePrice,
            status = a.Status.ToString(),
            locationId = a.LocationId,
            locationName = a.LocationId is { } l ? locations.GetValueOrDefault(l) : null,
            assignedEmployeeId = a.AssignedEmployeeId,
            assignedEmployeeName = a.AssignedEmployeeId is { } e ? employees.GetValueOrDefault(e) : null,
            assignedAtUtc = a.AssignedAtUtc,
            notes = a.Notes,
            createdAtUtc = a.CreatedAtUtc,
        }).ToList();
    }

    private async Task<bool> LocationExistsAsync(Guid? locationId, CancellationToken ct)
        => locationId is not { } id || await _db.Locations.AnyAsync(l => l.Id == id, ct);

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly? ParseDateOrNull(string? value)
        => DateOnly.TryParse(value, out var d) ? d : null;

    private static bool TryParseType(string? value, out AssetType type)
        => Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type);

    private static bool TryParseStatus(string? value, out AssetStatus status)
        => Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
}
