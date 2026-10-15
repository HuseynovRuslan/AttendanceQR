namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A named set of permissions inside one company (see AccessPermissions for the keys). Who holds it, and
/// where, is a <see cref="RoleAssignment"/>. docs/strukturlar-ve-selahiyyetler.md §3.2.
/// </summary>
public class AccessRole : ITenantScoped
{
    public AccessRole()
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The template it was made from (AccessRoleTemplates), or null for a role the company made.</summary>
    public string? TemplateKey { get; set; }

    /// <summary>Defined by the code and re-synced from it; not editable. See AccessRoleTemplate.IsLocked.</summary>
    public bool IsLocked { get; set; }

    /// <summary>See AccessRoleTemplate.PeersManageable.</summary>
    public bool PeersManageable { get; set; }

    /// <summary>Permission keys. Unknown keys are ignored when access is computed, never widened.</summary>
    public List<string> Permissions { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>A retired role grants nothing, wherever it is still assigned.</summary>
    public DateTime? ArchivedAtUtc { get; set; }
}
