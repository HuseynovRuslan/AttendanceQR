namespace AttendanceQR.Domain.Enums;

/// <summary>
/// Where a piece of equipment stands right now.
///
/// <see cref="Assigned"/> and <see cref="Domain.Entities.Asset.AssignedEmployeeId"/> move together —
/// one is never set without the other. The whole point of the module is answering "who has it", and a
/// row that says Assigned but names nobody answers it wrongly rather than not at all.
/// </summary>
public enum AssetStatus
{
    /// <summary>In the company's hands, nobody is using it.</summary>
    InStock = 0,

    /// <summary>Handed to an employee, who is responsible for it.</summary>
    Assigned = 1,

    InRepair = 2,

    /// <summary>Written off — kept as a row so the inventory history stays complete.</summary>
    WrittenOff = 3
}
