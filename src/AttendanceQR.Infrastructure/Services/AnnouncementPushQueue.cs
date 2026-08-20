using System.Threading.Channels;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>An announcement whose push should go out now, by tenant + id (the worker re-reads the
/// row, so a retire that lands first wins).</summary>
public sealed record AnnouncementPushJob(Guid TenantId, Guid AnnouncementId);

/// <summary>
/// Hands announcement push fan-out from the admin's HTTP request to a background worker. Inline, a
/// broadcast to a 2000-employee tenant (~1400+ subscriptions) took minutes inside one POST and was
/// cancelled by RequestAborted when the browser gave up — an unknowable prefix of the company got
/// the push and the rest never did.
/// </summary>
public interface IAnnouncementPushQueue
{
    void Enqueue(Guid tenantId, Guid announcementId);
    ChannelReader<AnnouncementPushJob> Reader { get; }
}

public sealed class AnnouncementPushQueue : IAnnouncementPushQueue
{
    // Unbounded is safe here: items are two Guids, and announcements are authored by hand.
    private readonly Channel<AnnouncementPushJob> _channel =
        Channel.CreateUnbounded<AnnouncementPushJob>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid tenantId, Guid announcementId)
        => _channel.Writer.TryWrite(new AnnouncementPushJob(tenantId, announcementId));

    public ChannelReader<AnnouncementPushJob> Reader => _channel.Reader;
}
