using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One line of a change request's history: who did what, when, and why. It is append-only. The code never
/// changes a line, and the database refuses an UPDATE (trg_ChangeRequestEvents_AppendOnly).
/// </summary>
public class ChangeRequestEvent : ITenantScoped
{
    public ChangeRequestEvent()
    {
        Id = Guid.NewGuid();
        AtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    public Guid ChangeRequestId { get; set; }

    public ChangeRequestEventKind Kind { get; set; }

    /// <summary>Null for a line the system wrote.</summary>
    public Guid? ActorEmployeeId { get; set; }

    public DateTime AtUtc { get; set; }

    public string? Note { get; set; }

    /// <summary>Extra facts as JSON, such as the refusal code of an ApplyRefused line.</summary>
    public string? Data { get; set; }
}
