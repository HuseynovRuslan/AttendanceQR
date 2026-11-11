namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A Company administrator standing in for a structure's operator for a limited time, for example while the
/// operator is on leave and the structure's requests must not wait.
///
/// The owner's rule (2026-11-11): only a Company administrator stands in, only through one of these, and the
/// stand-in ends by itself at <see cref="EndsAtUtc"/>. Nobody else's right ever widens on its own. A structure's
/// observer never starts deciding because the operator is away.
///
/// A substitution is revoked, never deleted.
/// </summary>
public class ApproverSubstitution : ITenantScoped
{
    public ApproverSubstitution()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    public Guid StructureId { get; set; }

    public Guid SubstituteEmployeeId { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    public string? Note { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? RevokedByEmployeeId { get; set; }
}
