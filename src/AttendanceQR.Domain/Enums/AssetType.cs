namespace AttendanceQR.Domain.Enums;

/// <summary>
/// What a piece of company equipment is. A fixed list rather than free text for the same reason job
/// titles became a catalogue: "noutbuk", "Noutbuk" and "laptop" would otherwise be three categories
/// in every count of what the company owns.
/// </summary>
public enum AssetType
{
    Laptop = 0,
    Desktop = 1,
    Monitor = 2,
    Printer = 3,
    Phone = 4,
    Tablet = 5,
    Server = 6,
    Network = 7,
    Peripheral = 8,
    Other = 9
}
