using System.Threading.Channels;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>One accepted check-in selfie awaiting upload: which record it belongs to, whose it is,
/// and the decoded bytes (validated in-request — the queue only ever holds plausible images).</summary>
public sealed record PhotoUploadJob(Guid TenantId, Guid RecordId, Guid EmployeeId, byte[] Bytes);

/// <summary>
/// Hands check-in selfies from the scan request to a background uploader, exactly like
/// <see cref="IFaceMatchQueue"/> hands off face matching. The scan response must never wait on R2:
/// with the upload in-request, a storage slowdown at shift start hung every check-in for its full
/// timeout AFTER the attendance row was already committed — the employee saw an error for a scan
/// that had in fact succeeded, and retried into <c>DuplicateCheckIn</c>.
/// </summary>
public interface IPhotoUploadQueue
{
    /// <summary>Non-blocking; false when the photo could not even be queued (the scan is never affected).</summary>
    bool TryEnqueue(PhotoUploadJob job);
    ChannelReader<PhotoUploadJob> Reader { get; }

    // Counters for /api/diag/queues — the only way to SEE this queue under load. All monotonic
    // except Depth. "Dropped" is deliberately not a counter: DropOldest evicts silently inside the
    // channel, so it is derived (Enqueued - Uploaded - Failed - Depth) at read time.
    long Enqueued { get; }
    long Uploaded { get; }
    long Failed { get; }
    int Depth { get; }
    void MarkUploaded();
    void MarkFailed();
}

public sealed class PhotoUploadQueue : IPhotoUploadQueue
{
    // Bounded because each item carries ~30-60KB of image: 500 items ≈ 30MB worst case, roughly a
    // whole shift-start burst. Past that the OLDEST photo is dropped — attendance is long since
    // committed either way, and under a real R2 outage newer selfies are the ones still worth
    // keeping when service returns.
    private readonly Channel<PhotoUploadJob> _channel = Channel.CreateBounded<PhotoUploadJob>(
        new BoundedChannelOptions(500) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    private long _enqueued;
    private long _uploaded;
    private long _failed;

    public bool TryEnqueue(PhotoUploadJob job)
    {
        var ok = _channel.Writer.TryWrite(job);
        if (ok) Interlocked.Increment(ref _enqueued);
        return ok;
    }

    public ChannelReader<PhotoUploadJob> Reader => _channel.Reader;

    public long Enqueued => Interlocked.Read(ref _enqueued);
    public long Uploaded => Interlocked.Read(ref _uploaded);
    public long Failed => Interlocked.Read(ref _failed);
    public int Depth => _channel.Reader.CanCount ? _channel.Reader.Count : -1;
    public void MarkUploaded() => Interlocked.Increment(ref _uploaded);
    public void MarkFailed() => Interlocked.Increment(ref _failed);
}
