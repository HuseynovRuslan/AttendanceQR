using System.Threading.Channels;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// In-process queue of AttendanceRecord ids awaiting a face-match. The check-in path enqueues (fast,
/// non-blocking); <c>FaceMatchWorker</c> drains it in the background. Unbounded and best-effort — a
/// process restart just drops pending items, which a manual "re-check" can re-queue.
/// </summary>
public interface IFaceMatchQueue
{
    void Enqueue(Guid recordId);
    ChannelReader<Guid> Reader { get; }
}

public sealed class FaceMatchQueue : IFaceMatchQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid recordId) => _channel.Writer.TryWrite(recordId);

    public ChannelReader<Guid> Reader => _channel.Reader;
}
