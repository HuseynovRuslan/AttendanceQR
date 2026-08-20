namespace AttendanceQR.Domain.Entities;

/// <summary>
/// A check-in selfie accepted from a scan but not yet safely in object storage. The row IS the
/// queue: written in the scan request (so a deploy, crash or R2 outage between the scan and the
/// upload can no longer lose the photo — the in-memory channel is only a latency hint), retried
/// with backoff by PhotoUploadWorker, and deleted the moment the upload succeeds — so the table's
/// steady state is empty and its size is bounded by the enqueue cap, not by history.
/// </summary>
public class PendingPhotoUpload : ITenantScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    /// <summary>The attendance record this selfie belongs to; the upload stamps its CheckInPhotoKey.</summary>
    public Guid RecordId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>The validated image itself (~30-60KB WebP). Lives only until the upload succeeds.</summary>
    public byte[] Bytes { get; set; } = [];

    /// <summary>Upload attempts so far. Past the worker's cap the row is dropped LOUDLY, never silently.</summary>
    public int Attempts { get; set; }

    /// <summary>When the next attempt is allowed — exponential backoff writes this forward.</summary>
    public DateTime NextAttemptUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
