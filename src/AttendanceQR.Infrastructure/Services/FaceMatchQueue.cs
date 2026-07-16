using System.Threading.Channels;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>One queued face-match: the record to check, plus the tenant it belongs to. The worker
/// drains this on its own scope with no request behind it, so it cannot infer the tenant — it has to
/// be carried over from the check-in that enqueued the work.</summary>
public readonly record struct FaceMatchJob(Guid TenantId, Guid RecordId);

/// <summary>
/// In-process queue of check-ins awaiting a face-match. The check-in path enqueues (fast,
/// non-blocking); <c>FaceMatchWorker</c> drains it in the background. Unbounded and best-effort — a
/// process restart just drops pending items, which a manual "re-check" can re-queue.
/// </summary>
public interface IFaceMatchQueue
{
    void Enqueue(Guid tenantId, Guid recordId);
    ChannelReader<FaceMatchJob> Reader { get; }
}

public sealed class FaceMatchQueue : IFaceMatchQueue
{
    private readonly Channel<FaceMatchJob> _channel =
        Channel.CreateUnbounded<FaceMatchJob>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid tenantId, Guid recordId)
        => _channel.Writer.TryWrite(new FaceMatchJob(tenantId, recordId));

    public ChannelReader<FaceMatchJob> Reader => _channel.Reader;
}
