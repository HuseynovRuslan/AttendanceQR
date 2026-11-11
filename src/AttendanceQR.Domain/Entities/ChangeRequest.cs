using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A change somebody asked for and somebody else decides. Today there is one kind: «add this person to my
/// branch» (<see cref="ChangeRequestType.EmployeeCreate"/>). See docs/tesdiq-sistemi-faza1.md.
///
/// Only branches inside an activated approval area (<see cref="ApprovalArea"/>) take requests. Everywhere else
/// the same change is made directly, exactly as before. The row is never deleted: its <see cref="Status"/> says
/// what happened to it, and its <see cref="ChangeRequestEvent"/> lines say how it got there.
/// </summary>
public class ChangeRequest : ITenantScoped
{
    public ChangeRequest()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    /// <summary>Sequential inside the company. It is what people say on the phone («R-1041»), and it is unique per
    /// company. A Direct row has none: nobody asked, so there is nothing to refer to.</summary>
    public int? Number { get; set; }

    public ChangeRequestType Type { get; set; }

    public ChangeRequestStatus Status { get; set; }

    /// <summary>Not a request at all. Somebody who may decide made the change themselves, and it is recorded here
    /// so that an approval area's history is in one place. Always Approved.</summary>
    public bool Direct { get; set; }

    /// <summary>The branch the change is about. For a new person, it is the branch they will work at. Both who may
    /// ask and who may decide are answered for this branch.</summary>
    public Guid LocationId { get; set; }

    /// <summary>The branch's structure when the request was made. This is history only: who decides is read from
    /// the branch's structure at the moment of the decision.</summary>
    public Guid? StructureIdAtCreation { get; set; }

    /// <summary>The person the change is about. For a new person, it is the account the approval created. It is
    /// empty while the request is pending, and empty again if that account is ever deleted (the payload keeps the
    /// name).</summary>
    public Guid? EmployeeId { get; set; }

    /// <summary>What is asked for, as JSON, in the type's own shape (EmployeeCreatePayload for a new person).</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>«Qeyd / Səbəb»: what the person asking wrote.</summary>
    public string? Note { get; set; }

    /// <summary>What makes two pending requests the same request. For a new person, it is their phone, or their
    /// e-mail if there is no phone. A unique index over pending rows refuses the second one.</summary>
    public string? DedupeKey { get; set; }

    /// <summary>The client's key for «this is the same press of the button». A retried create returns the request
    /// the first one made instead of making a second. Unique per company.</summary>
    public string? IdempotencyKey { get; set; }

    public Guid RequestedByEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public Guid? DecidedByEmployeeId { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>The approver's note, the rejection's reason (required), or the note of the person who cancelled.</summary>
    public string? DecisionNote { get; set; }

    /// <summary>When the approved change was written. It is set together with Approved, in the same transaction.</summary>
    public DateTime? AppliedAtUtc { get; set; }

    /// <summary>When the approvers were told about it. Null until the notification worker has gone over it.</summary>
    public DateTime? NotifiedAtUtc { get; set; }

    /// <summary>The Baku day on which the «waiting more than 24 hours» reminder last went out for it.</summary>
    public DateOnly? RemindedOn { get; set; }

    /// <summary>Optimistic concurrency. Every move bumps it, and a move made from a stale copy fails. When two
    /// approvers press at once, one gets the decision and the other gets «already decided».</summary>
    public int Version { get; set; }
}
