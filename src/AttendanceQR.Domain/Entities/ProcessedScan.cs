namespace AttendanceQR.Domain.Entities;

/// <summary>
/// Idempotency ledger for offline scans. Each queued scan carries a client-generated
/// <see cref="ClientScanId"/>; the first time the server processes it, a row lands here. A retry
/// (the app re-sending a queued scan, or a lost response being replayed) finds the row and is answered
/// "already recorded" instead of creating a second check-in/out. Tenant-scoped.
/// </summary>
public class ProcessedScan : ITenantScoped
{
    public ProcessedScan()
    {
        Id = Guid.NewGuid();
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    // The client-generated id of the scan action (one per tap, reused across retries). Unique per
    // tenant — see the (TenantId, ClientScanId) unique index.
    public Guid ClientScanId { get; set; }

    public Guid EmployeeId { get; set; }

    public DateTime ProcessedAtUtc { get; set; }
}
