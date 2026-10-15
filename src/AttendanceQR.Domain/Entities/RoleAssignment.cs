using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One person holding one role over one area: the whole company, one structure, or one branch. A person
/// can hold any number — an operator over «Aeroport yolu» and an observer at one more branch is two rows.
///
/// Revoked, never deleted: the row stays with who took it away and when, so «who could do this on the
/// 3rd» has an answer. docs/strukturlar-ve-selahiyyetler.md §3.2.
/// </summary>
public class RoleAssignment : ITenantScoped
{
    public RoleAssignment()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public Guid AccessRoleId { get; set; }

    public AccessScopeType Scope { get; set; }

    /// <summary>Set exactly when <see cref="Scope"/> is Structure. The database checks the pairing.</summary>
    public Guid? StructureId { get; set; }

    /// <summary>Set exactly when <see cref="Scope"/> is Branch. The database checks the pairing.</summary>
    public Guid? LocationId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Null when the system made it (the migration of today's Admin and Manager access).</summary>
    public Guid? CreatedByEmployeeId { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? RevokedByEmployeeId { get; set; }
}
