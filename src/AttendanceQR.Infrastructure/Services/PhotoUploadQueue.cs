using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>A pointer to one PendingPhotoUpload row — the photo itself is already safe in the DB.</summary>
public sealed record PhotoUploadHint(Guid TenantId, Guid PendingId);

/// <summary>
/// Coordination for the DURABLE photo-upload queue. The queue itself is the PendingPhotoUploads
/// table — written in the scan request, deleted on successful upload — so a deploy, crash or R2
/// outage can no longer lose a photo. What lives here is only what must be process-local:
/// the in-memory hint channel (so a healthy upload starts milliseconds after the scan instead of
/// on the next poll), the in-flight set (so the poller and the hint path never double-upload one
/// row), the counters for /api/diag/queues, and the approximate pending total that enforces the
/// enqueue cap without a COUNT per scan.
/// </summary>
public interface IPhotoUploadQueue
{
    /// <summary>Latency hint only — losing it costs nothing but a poll interval.</summary>
    void HintReady(PhotoUploadHint hint);
    ChannelReader<PhotoUploadHint> Reader { get; }

    /// <summary>Claim a row for processing; false = another lane already has it.</summary>
    bool TryBeginInFlight(Guid pendingId);
    void EndInFlight(Guid pendingId);

    // Counters (process lifetime): every accepted photo ends in exactly one of uploaded, failed
    // (permanent, after the retry budget) or dropped (cap/age/record-gone) — nothing silent.
    long Enqueued { get; }
    long Uploaded { get; }
    long Retries { get; }
    long Failed { get; }
    long Dropped { get; }
    void MarkEnqueued();
    void MarkUploaded();
    void MarkRetry();
    void MarkFailed();
    void MarkDropped();

    /// <summary>Approximate rows currently pending across all tenants (poller reconciles it with the
    /// DB). -1 until the first reconciliation.</summary>
    long PendingApprox { get; }
    void PendingDelta(int delta);
    void PendingReconcile(long exact);
}

public sealed class PhotoUploadQueue : IPhotoUploadQueue
{
    // Hints are two Guids; the bound only guards against a runaway. DropOldest is safe — the poller
    // is the source of truth, a dropped hint just waits for it.
    private readonly Channel<PhotoUploadHint> _channel = Channel.CreateBounded<PhotoUploadHint>(
        new BoundedChannelOptions(4000) { SingleReader = false, FullMode = BoundedChannelFullMode.DropOldest });

    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();
    private long _enqueued, _uploaded, _retries, _failed, _dropped;
    private long _pendingApprox = -1;

    public void HintReady(PhotoUploadHint hint) => _channel.Writer.TryWrite(hint);
    public ChannelReader<PhotoUploadHint> Reader => _channel.Reader;

    public bool TryBeginInFlight(Guid pendingId) => _inFlight.TryAdd(pendingId, 0);
    public void EndInFlight(Guid pendingId) => _inFlight.TryRemove(pendingId, out _);

    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Uploaded => Interlocked.Read(ref _uploaded);
    public long Retries => Interlocked.Read(ref _retries);
    public long Failed => Interlocked.Read(ref _failed);
    public long Dropped => Interlocked.Read(ref _dropped);
    public void MarkEnqueued() => Interlocked.Increment(ref _enqueued);
    public void MarkUploaded() => Interlocked.Increment(ref _uploaded);
    public void MarkRetry() => Interlocked.Increment(ref _retries);
    public void MarkFailed() => Interlocked.Increment(ref _failed);
    public void MarkDropped() => Interlocked.Increment(ref _dropped);

    public long PendingApprox => Interlocked.Read(ref _pendingApprox);
    public void PendingDelta(int delta)
    {
        if (Interlocked.Read(ref _pendingApprox) >= 0)
            Interlocked.Add(ref _pendingApprox, delta);
    }
    public void PendingReconcile(long exact) => Interlocked.Exchange(ref _pendingApprox, exact);
}
