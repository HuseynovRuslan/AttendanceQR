namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One line of the company's IT equipment register: a person, and everything they hold.
///
/// This deliberately mirrors the spreadsheet the register already lives in ("İT AVADANLIQLARININ
/// SİYAHISI") rather than modelling one row per device. The source has no inventory numbers and
/// describes kit in prose — "1 ədəd masaüstü ofis kompüteri, 2 ədəd monitor HP 27\"" — so splitting a
/// line into separate devices would mean inventing identifiers and guessing counts out of free text.
/// The register is imported from, and compared against, that file; keeping the same shape is what
/// makes a re-import a diff instead of a reconciliation exercise.
///
/// <see cref="EmployeeId"/> is an optional link to the staff list. It is set when the imported name
/// matches an employee exactly; unmatched rows keep their name as text rather than being dropped,
/// because the register has to cover people the attendance system does not (contractors, someone who
/// left with a laptop still out).
/// </summary>
public class EquipmentRecord : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>"Sıra №" — the line number in the source list. Unique per company: it is what a
    /// re-import matches on, so an updated file edits its rows instead of duplicating them.</summary>
    public int RowNo { get; set; }

    /// <summary>"Soyadı, adı, atasının adı" — as written in the register.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>"Vəzifəsi".</summary>
    public string? Position { get; set; }

    /// <summary>"İşlədiyi ərazi" — the office or site, free text (it names places that are not
    /// branches in the attendance system: "Bərpa işləri", "Ümumi ərazilər").</summary>
    public string? Area { get; set; }

    /// <summary>"Avadanlıq" — the summary line: what the person has, in words.</summary>
    public string? Equipment { get; set; }

    /// <summary>"Sistem bloku" — the desktop's specification, one line per machine.</summary>
    public string? SystemUnit { get; set; }

    /// <summary>"Monitor".</summary>
    public string? Monitor { get; set; }

    /// <summary>"Digər avadanlıq" — laptops, printers, scanners.</summary>
    public string? OtherEquipment { get; set; }

    /// <summary>The staff record this line belongs to, when the name matched one. Null is normal.</summary>
    public Guid? EmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
