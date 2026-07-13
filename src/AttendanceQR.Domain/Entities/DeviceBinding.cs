using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Domain.Entities;

public class DeviceBinding
{
    public DeviceBinding()
    {
        Id = Guid.NewGuid();
        BoundAtUtc = DateTime.UtcNow;
        LastSeenAtUtc = BoundAtUtc;
    }

    public Guid Id { get; set; }

    // Multi-tenancy: which company (Tenant) this row belongs to.
    public Guid TenantId { get; set; }

    // FK to Employee. An employee holds SEVERAL bindings — one per browser storage context (Safari,
    // the installed PWA, a spare phone). The web exposes no cross-browser device identity, so a
    // single binding would lock the employee out the moment they switch context.
    public Guid EmployeeId { get; set; }

    public string DeviceFingerprint { get; set; } = string.Empty;

    // Human-friendly device name — from the User-Agent at activation, or derived server-side when a
    // device is auto-bound. Shown in the admin employee list. Never used for any security decision;
    // DeviceFingerprint is the only value scan actually matches against.
    public string? DeviceLabel { get; set; }

    public DateTime BoundAtUtc { get; set; }

    // Bumped on every accepted scan. Decides the eviction order once MaxActiveDevices is reached.
    public DateTime LastSeenAtUtc { get; set; }

    public DeviceBindingOrigin BoundVia { get; set; } = DeviceBindingOrigin.Activation;

    // Set when an admin deliberately kills this context. Distinct from IsActive=false by eviction:
    // an evicted context is re-adopted the next time it scans, a REVOKED one never is — only an
    // admin approval brings it back. Without this, "revoke" would be undone by the next scan.
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}
