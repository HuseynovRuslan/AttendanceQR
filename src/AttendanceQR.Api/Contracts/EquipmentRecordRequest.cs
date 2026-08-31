namespace AttendanceQR.Api.Contracts;

/// <summary>
/// One line of the IT equipment register. The field names follow the register's own columns:
/// Sıra № · Soyadı, adı, atasının adı · Vəzifəsi · İşlədiyi ərazi · Avadanlıq · Sistem bloku ·
/// Monitor · Digər avadanlıq.
///
/// <c>RowNo</c> is nullable: on create, null means "append to the end of the list" — someone adding
/// one person is not thinking about line numbers.
/// </summary>
public record EquipmentRecordRequest(
    int? RowNo,
    string FullName,
    string? Position,
    string? Area,
    string? Equipment,
    string? SystemUnit,
    string? Monitor,
    string? OtherEquipment,
    Guid? EmployeeId);
