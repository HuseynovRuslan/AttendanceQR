namespace AttendanceQR.Domain.Entities;

/// <summary>
/// One kind of disagreement between today's authorization and the permission model, while a company is in
/// Shadow mode — counted per day rather than written per request, so a screen polled every ten seconds
/// cannot fill the table. Nothing reads it but the people deciding whether the company may switch.
/// </summary>
public class AuthzShadowEvent : ITenantScoped
{
    public AuthzShadowEvent()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    // Owning tenant (company). See ITenantScoped.
    public Guid TenantId { get; set; }

    public DateOnly Day { get; set; }

    /// <summary>«Gate» (may this person call this endpoint at all), «Twin» (the same, where the other role has
    /// its own route doing the same thing), or the name of a scope rule (CanManage, CanAccess, …).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The endpoint or rule detail the decision was about.</summary>
    public string Subject { get; set; } = string.Empty;

    public Guid EmployeeId { get; set; }

    /// <summary>Who or what it was about — an employee id, a branch set — or empty for a gate.</summary>
    public string TargetKey { get; set; } = string.Empty;

    public bool Legacy { get; set; }

    public bool New { get; set; }

    public int Count { get; set; }

    public DateTime FirstAtUtc { get; set; }

    public DateTime LastAtUtc { get; set; }
}
