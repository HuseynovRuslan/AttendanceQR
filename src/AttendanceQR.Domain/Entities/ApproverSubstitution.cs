namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An existing Operator deciding one more structure's requests for a limited time, for example while that
/// structure's own operator is on leave and its requests must not wait.
///
/// The owner's rule (2026-11-11, revised the same day): a Company administrator picks somebody who is already an
/// Operator elsewhere (holds «requests.decide» over a structure or a branch) and names the structure and the end.
/// It lasts at most 31 days and ends by itself at <see cref="EndsAtUtc"/>. It gives the right to decide that
/// structure's requests and to read what is decided, and nothing else: no direct changes there, no request of
/// one's own there, no other screen. Nobody's right ever widens on its own: an observer is never made an
/// approver, and a person's own request stays somebody else's to decide.
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
