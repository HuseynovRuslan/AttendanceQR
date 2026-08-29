using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A piece of company equipment — a laptop, a monitor, a printer — and who is currently responsible
/// for it.
///
/// The card is deliberately flat: the inventory number is what is written on the sticker stuck to the
/// device, and it is the only identifier a storekeeper reads out loud, so it is unique per company and
/// required. Serial numbers are not: plenty of kit arrives without a readable one, and refusing to
/// register a monitor because its label has rubbed off would push the whole list back into a
/// spreadsheet.
///
/// <see cref="AssignedEmployeeId"/> and <see cref="Status"/> are kept consistent by the controller:
/// assigning sets both, returning clears both. The employee FK is Restrict, so an employee still
/// holding equipment cannot be deleted out from under it — someone has to hand the laptop back first.
/// </summary>
public class Asset : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>The number on the sticker. Unique within the company.</summary>
    public string InventoryNumber { get; set; } = string.Empty;

    public AssetType Type { get; set; }

    /// <summary>What it is called in conversation, e.g. "Dell Latitude 5420".</summary>
    public string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    public DateOnly? PurchaseDate { get; set; }

    /// <summary>Purchase price in AZN. Optional — older kit is often on the books without one.</summary>
    public decimal? PurchasePrice { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.InStock;

    /// <summary>Which branch the equipment physically sits at. Optional: a company on one site has no
    /// use for it, and equipment in transit belongs to no branch at all.</summary>
    public Guid? LocationId { get; set; }

    /// <summary>Null unless <see cref="Status"/> is <see cref="AssetStatus.Assigned"/>.</summary>
    public Guid? AssignedEmployeeId { get; set; }

    /// <summary>When it was handed over. Null while unassigned.</summary>
    public DateTime? AssignedAtUtc { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
