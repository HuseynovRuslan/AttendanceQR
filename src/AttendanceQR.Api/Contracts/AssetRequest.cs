namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Create or update an equipment card. Assignment is deliberately not part of this — handing a laptop
/// to someone and correcting its serial number are different acts, and folding them into one payload
/// is how a typo in an edit form silently reassigns a device.
///
/// <c>Type</c> and <c>Status</c> arrive as enum names ("Laptop", "InStock"); <c>PurchaseDate</c> as
/// "yyyy-MM-dd" — what an &lt;input type="date"&gt; emits — or null.
/// </summary>
public record AssetRequest(
    string InventoryNumber,
    string Type,
    string Name,
    string? Brand,
    string? Model,
    string? SerialNumber,
    string? PurchaseDate,
    decimal? PurchasePrice,
    string? Status,
    Guid? LocationId,
    string? Notes);

/// <summary>Hand a piece of equipment to an employee. Notes replaces the card's note when given.</summary>
public record AssetAssignRequest(Guid EmployeeId, string? Notes);
