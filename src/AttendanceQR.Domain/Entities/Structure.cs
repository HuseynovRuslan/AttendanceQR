namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A group of branches inside one company — «Aeroport yolu», «Şəhər parkları» — that one operator looks
/// after. Optional: a company with no structures works exactly as before, and a branch belongs to at most
/// one (<see cref="Location.StructureId"/>).
///
/// Archived, never deleted. A structure is only a label over branches; the people, their days and the
/// branches themselves never hang off it, so nothing is lost when it goes — and an archived name stays
/// readable in the audit trail of the moves that were made under it.
/// </summary>
public class Structure : ITenantScoped
{
    public Structure()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when the structure is retired. Only an empty structure can be archived, so this never
    /// strands a branch; while set, no branch can be put into it.</summary>
    public DateTime? ArchivedAtUtc { get; set; }
}
