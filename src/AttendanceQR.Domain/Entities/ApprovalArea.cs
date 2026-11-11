using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A structure, or one branch of a structure, where changes go through requests. Nothing else switches the
/// approval flow on. A company with no live area works exactly as before. There is deliberately no company-wide
/// switch: the owner turns the flow on where an operator is ready to answer (decision of 2026-11-11).
///
/// A branch that belongs to no structure is never in an area. It stays on today's flow, and its changes are not
/// sent to anybody for approval.
///
/// An area is deactivated, never deleted, so «was this branch under approval on the 3rd» has an answer.
/// </summary>
public class ApprovalArea : ITenantScoped
{
    public ApprovalArea()
    {
        Id = Guid.NewGuid();
        ActivatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    /// <summary>Structure or Branch, never Company. The database checks that it matches the ids below.</summary>
    public AccessScopeType Scope { get; set; }

    public Guid? StructureId { get; set; }

    public Guid? LocationId { get; set; }

    public DateTime ActivatedAtUtc { get; set; }

    public Guid ActivatedByEmployeeId { get; set; }

    public DateTime? DeactivatedAtUtc { get; set; }

    public Guid? DeactivatedByEmployeeId { get; set; }
}
