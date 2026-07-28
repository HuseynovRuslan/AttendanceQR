using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An employee who forgot their PIN — and so cannot sign in — asks from the login screen for it to be
/// reset. The request lands in an admin queue; the admin resets the PIN (getting a temporary one to
/// pass on) and marks it Resolved, or Dismisses a bogus one. It grants nothing itself.
///
/// Created ANONYMOUSLY (the employee isn't logged in), so it carries the employee id resolved from the
/// identifier they typed, not from a token. Tenant is stamped from the subdomain like every other row.
/// </summary>
public class PinResetRequest : ITenantScoped
{
    public PinResetRequest()
    {
        Id = Guid.NewGuid();
        RequestedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    // The employee asking for the reset (resolved from the identifier at request time).
    public Guid EmployeeId { get; set; }

    public PinResetStatus Status { get; set; } = PinResetStatus.Pending;

    public DateTime RequestedAtUtc { get; set; }

    // The admin who resolved/dismissed it, and when. Null while pending.
    public Guid? ResolvedByEmployeeId { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }
}
