using AttendanceQR.Api.Contracts;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api.Controllers;

/// <summary>
/// The IT equipment register — the "İT AVADANLIQLARININ SİYAHISI" list, one line per person.
///
/// Admin only. A branch manager sees who is at work; the register spans every office and site and is
/// not a branch-scoped screen.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/equipment")]
public class AdminEquipmentController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminEquipmentController(AppDbContext db) => _db = db;

    /// <summary>The register, in the order of the source list. `q` searches every text column at once —
    /// a name, a place, or "RTX 4090" all reach the same box.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q)
    {
        var ct = HttpContext.RequestAborted;
        var query = _db.EquipmentRecords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.FullName, pattern)
                || (r.Position != null && EF.Functions.ILike(r.Position, pattern))
                || (r.Area != null && EF.Functions.ILike(r.Area, pattern))
                || (r.Equipment != null && EF.Functions.ILike(r.Equipment, pattern))
                || (r.SystemUnit != null && EF.Functions.ILike(r.SystemUnit, pattern))
                || (r.Monitor != null && EF.Functions.ILike(r.Monitor, pattern))
                || (r.OtherEquipment != null && EF.Functions.ILike(r.OtherEquipment, pattern)));
        }

        var records = await query.OrderBy(r => r.RowNo).ToListAsync(ct);
        return Ok(records.Select(ToDto));
    }

    /// <summary>Everything one employee holds — what the staff profile shows.</summary>
    [HttpGet("by-employee/{employeeId:guid}")]
    public async Task<IActionResult> ByEmployee(Guid employeeId)
    {
        var ct = HttpContext.RequestAborted;
        var records = await _db.EquipmentRecords
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.RowNo)
            .ToListAsync(ct);
        return Ok(records.Select(ToDto));
    }

    /// <summary>Headline counts. "Unmatched" is the one that earns its place: it is how many lines name
    /// somebody the staff list does not have, which is the register's usual way of being out of date.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var ct = HttpContext.RequestAborted;
        var records = await _db.EquipmentRecords
            .Select(r => new { r.EmployeeId, r.Area, r.SystemUnit, r.OtherEquipment })
            .ToListAsync(ct);

        return Ok(new
        {
            total = records.Count,
            linked = records.Count(r => r.EmployeeId != null),
            unlinked = records.Count(r => r.EmployeeId == null),
            areas = records.Where(r => !string.IsNullOrWhiteSpace(r.Area))
                .Select(r => r.Area!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            withDesktop = records.Count(r => !string.IsNullOrWhiteSpace(r.SystemUnit)),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EquipmentRecordRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var fullName = (request.FullName ?? string.Empty).Trim();
        if (fullName.Length == 0) return BadRequest(new { error = "FullNameRequired" });

        // A blank row number means "put it at the end" rather than a validation error — someone adding
        // one person is not thinking about line numbers.
        var rowNo = request.RowNo ?? await NextRowNoAsync(ct);
        if (await _db.EquipmentRecords.AnyAsync(r => r.RowNo == rowNo, ct))
            return Conflict(new { error = "RowNoExists" });
        if (!await EmployeeExistsAsync(request.EmployeeId, ct))
            return BadRequest(new { error = "EmployeeNotFound" });

        var record = new EquipmentRecord
        {
            RowNo = rowNo,
            FullName = fullName,
            Position = Trimmed(request.Position),
            Area = Trimmed(request.Area),
            Equipment = Trimmed(request.Equipment),
            SystemUnit = Trimmed(request.SystemUnit),
            Monitor = Trimmed(request.Monitor),
            OtherEquipment = Trimmed(request.OtherEquipment),
            EmployeeId = request.EmployeeId,
        };

        _db.EquipmentRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(record));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EquipmentRecordRequest request)
    {
        var ct = HttpContext.RequestAborted;
        var record = await _db.EquipmentRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null) return NotFound(new { error = "RecordNotFound" });

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (fullName.Length == 0) return BadRequest(new { error = "FullNameRequired" });

        var rowNo = request.RowNo ?? record.RowNo;
        if (rowNo != record.RowNo && await _db.EquipmentRecords.AnyAsync(r => r.Id != id && r.RowNo == rowNo, ct))
            return Conflict(new { error = "RowNoExists" });
        if (!await EmployeeExistsAsync(request.EmployeeId, ct))
            return BadRequest(new { error = "EmployeeNotFound" });

        record.RowNo = rowNo;
        record.FullName = fullName;
        record.Position = Trimmed(request.Position);
        record.Area = Trimmed(request.Area);
        record.Equipment = Trimmed(request.Equipment);
        record.SystemUnit = Trimmed(request.SystemUnit);
        record.Monitor = Trimmed(request.Monitor);
        record.OtherEquipment = Trimmed(request.OtherEquipment);
        record.EmployeeId = request.EmployeeId;
        record.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ToDto(record));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ct = HttpContext.RequestAborted;
        var record = await _db.EquipmentRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (record is null) return NotFound(new { error = "RecordNotFound" });

        _db.EquipmentRecords.Remove(record);
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = id });
    }

    /// <summary>
    /// Reads the register spreadsheet and writes it into the table.
    ///
    /// Rows are matched on "Sıra №", so re-uploading an edited file updates the lines it already has
    /// instead of doubling the register — which is the whole point: the spreadsheet stays the working
    /// document, and this screen stays a view of it that everyone can read.
    ///
    /// Lines the file no longer contains are left alone rather than deleted. A partial file (one
    /// office's sheet, say) must not be able to wipe the rest of the register, and an admin who really
    /// wants a line gone can delete it.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        var ct = HttpContext.RequestAborted;
        if (file is null || file.Length == 0) return BadRequest(new { error = "NoFile" });

        EquipmentSheetError sheetError;
        List<EquipmentSheetRow> parsed;
        try
        {
            using var stream = file.OpenReadStream();
            (sheetError, parsed) = EquipmentSheet.Parse(stream);
        }
        catch (Exception)
        {
            // ClosedXML throws a whole family of exceptions on a file that is not really a workbook.
            // The admin only needs to know the file could not be read.
            return BadRequest(new { error = "UnreadableFile" });
        }

        if (sheetError != EquipmentSheetError.Ok)
            return BadRequest(new { error = sheetError.ToString() });

        if (parsed.Count == 0) return Ok(new { added = 0, updated = 0, linked = 0, unmatched = Array.Empty<string>() });

        // A name is matched to staff only when it is written identically (ignoring case, punctuation
        // and repeated spaces). No fuzzy matching: the register writes surname first with a
        // patronymic, the staff list often does not, and a near-miss would attach one person's laptops
        // to another — a wrong link is worse than none, because nobody goes looking for it.
        var employees = await _db.Employees
            .Select(e => new { e.Id, e.FullName })
            .ToListAsync(ct);
        var byName = new Dictionary<string, Guid>();
        foreach (var e in employees)
        {
            var key = NormalizeName(e.FullName);
            if (key.Length > 0) byName.TryAdd(key, e.Id);
        }

        var existing = await _db.EquipmentRecords.ToDictionaryAsync(r => r.RowNo, ct);
        int added = 0, updated = 0, linked = 0;
        var unmatched = new List<string>();

        foreach (var p in parsed)
        {
            var employeeId = byName.TryGetValue(NormalizeName(p.FullName), out var id) ? id : (Guid?)null;
            if (employeeId is not null) linked++;
            else unmatched.Add(p.FullName);

            if (existing.TryGetValue(p.RowNo, out var record))
            {
                record.FullName = p.FullName;
                record.Position = p.Position;
                record.Area = p.Area;
                record.Equipment = p.Equipment;
                record.SystemUnit = p.SystemUnit;
                record.Monitor = p.Monitor;
                record.OtherEquipment = p.OtherEquipment;
                // Keep a link an admin made by hand when the import cannot find one itself.
                record.EmployeeId = employeeId ?? record.EmployeeId;
                record.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }
            else
            {
                _db.EquipmentRecords.Add(new EquipmentRecord
                {
                    RowNo = p.RowNo,
                    FullName = p.FullName,
                    Position = p.Position,
                    Area = p.Area,
                    Equipment = p.Equipment,
                    SystemUnit = p.SystemUnit,
                    Monitor = p.Monitor,
                    OtherEquipment = p.OtherEquipment,
                    EmployeeId = employeeId,
                });
                added++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { added, updated, linked, unmatched });
    }

    // --- helpers -------------------------------------------------------------

    /// <summary>Case- and spacing-insensitive form of a name, for exact matching only.</summary>
    private static string NormalizeName(string? name)
        => string.IsNullOrWhiteSpace(name)
            ? string.Empty
            : string.Join(' ', name.Split([' ', ',', '.', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .ToLowerInvariant();

    private async Task<int> NextRowNoAsync(CancellationToken ct)
        => await _db.EquipmentRecords.AnyAsync(ct)
            ? await _db.EquipmentRecords.MaxAsync(r => r.RowNo, ct) + 1
            : 1;

    private async Task<bool> EmployeeExistsAsync(Guid? employeeId, CancellationToken ct)
        => employeeId is not { } id || await _db.Employees.AnyAsync(e => e.Id == id, ct);

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object ToDto(EquipmentRecord r) => new
    {
        id = r.Id,
        rowNo = r.RowNo,
        fullName = r.FullName,
        position = r.Position,
        area = r.Area,
        equipment = r.Equipment,
        systemUnit = r.SystemUnit,
        monitor = r.Monitor,
        otherEquipment = r.OtherEquipment,
        employeeId = r.EmployeeId,
        updatedAtUtc = r.UpdatedAtUtc,
    };
}
