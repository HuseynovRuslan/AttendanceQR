namespace AttendanceQR.Domain.Entities;

/// <summary>
/// Admin-granted permission: <see cref="AssignerEmployeeId"/> may hand tasks to
/// <see cref="RecipientEmployeeId"/>. One row per (assigner, recipient) pair.
///
/// This is what makes the Tapşırıqlar section NOT apply to everyone: the admin decides, per person,
/// who can give tasks and to whom. Someone is a "giver" if they have ≥1 row as assigner; a "recipient"
/// if they have ≥1 row as recipient. The Tasks section is shown only to admins, givers and recipients.
/// </summary>
public class TaskAssignPermission : ITenantScoped
{
    public TaskAssignPermission()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to. Auto-stamped on insert.
    public Guid TenantId { get; set; }

    public Guid AssignerEmployeeId { get; set; }
    public Guid RecipientEmployeeId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
