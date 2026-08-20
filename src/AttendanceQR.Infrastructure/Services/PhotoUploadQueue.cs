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
    /// <summary>Non-blocking; false when the queue is full (the photo is dropped, never the scan).</summary>
    bool TryEnqueue(PhotoUploadJob job);
    ChannelReader<PhotoUploadJob> Reader { get; }
}

public sealed class PhotoUploadQueue : IPhotoUploadQueue
{
    // Bounded because each item carries ~30-60KB of image: 500 items ≈ 30MB worst case, roughly a
    // whole shift-start burst. Past that the OLDEST photo is dropped — attendance is long since
    // committed either way, and under a real R2 outage newer selfies are the ones still worth
    // keeping when service returns.
    private readonly Channel<PhotoUploadJob> _channel = Channel.CreateBounded<PhotoUploadJob>(
        new BoundedChannelOptions(500) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    public bool TryEnqueue(PhotoUploadJob job) => _channel.Writer.TryWrite(job);

    public ChannelReader<PhotoUploadJob> Reader => _channel.Reader;
}
