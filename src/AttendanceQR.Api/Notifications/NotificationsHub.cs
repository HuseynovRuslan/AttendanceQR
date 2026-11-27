using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AttendanceQR.Api.Notifications;

/// <summary>
/// The live channel of the notification module: an open admin panel hears its own new notifications the moment they
/// are written, instead of on its next poll.
///
/// Server → client only; the hub has no methods a client can call. A connection is registered under exactly one
/// session — company, person, token version — taken from the server-issued ticket, never from anything the client says,
/// so nobody can listen to another person's or another company's notifications, and a session that has ended hears
/// nothing more (<see cref="LiveConnections"/>). It authenticates with a short-lived ticket (<see cref="HubTickets"/>),
/// not the session JWT: a browser's WebSocket carries the credential in the URL, URLs end up in logs, and these JWTs live
/// for years.
/// </summary>
[Authorize(AuthenticationSchemes = HubTicketAuthentication.SchemeName)]
public sealed class NotificationsHub : Hub
{
    public const string Path = "/hubs/notifications";

    /// <summary>The one event the client listens to.</summary>
    public const string Event = "notifications";

    private readonly LiveConnections _connections;

    public NotificationsHub(LiveConnections connections) => _connections = connections;

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (!Guid.TryParse(user?.FindFirstValue("tid"), out var tenantId)
            || !Guid.TryParse(user?.FindFirstValue("sub"), out var employeeId)
            || !int.TryParse(user?.FindFirstValue("tv"), out var tokenVersion))
        {
            Context.Abort();
            return;
        }
        var context = Context;
        _connections.Add(context.ConnectionId, new LiveConnections.Session(tenantId, employeeId, tokenVersion), context.Abort);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

/// <summary>Sends live notifications to whoever has the panel open. Never throws: a dropped pop-up is found in the center.</summary>
public interface INotificationLive
{
    /// <summary>
    /// <paramref name="sessions"/>: each recipient with a valid session now and the token version it carries
    /// (<see cref="LiveSessions.ReadAsync"/>, read just before this call). A recipient missing from it hears nothing,
    /// and a connection opened under an older session is closed instead of being sent to.
    /// </summary>
    Task SendAsync(Guid tenantId, IReadOnlyList<LiveNotification> items, IReadOnlyDictionary<Guid, int> sessions, CancellationToken ct = default);
}

public sealed class SignalRNotificationLive : INotificationLive
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly LiveConnections _connections;
    private readonly ILogger<SignalRNotificationLive> _logger;

    public SignalRNotificationLive(IHubContext<NotificationsHub> hub, LiveConnections connections, ILogger<SignalRNotificationLive> logger)
    {
        _hub = hub;
        _connections = connections;
        _logger = logger;
    }

    public async Task SendAsync(
        Guid tenantId, IReadOnlyList<LiveNotification> items, IReadOnlyDictionary<Guid, int> sessions, CancellationToken ct = default)
    {
        foreach (var group in items.GroupBy(i => i.RecipientId))
        {
            try
            {
                var to = _connections.Current(tenantId, group.Key, sessions.TryGetValue(group.Key, out var v) ? v : null);
                if (to.Count == 0)
                    continue;
                // Only what the pop-up shows; the click target is re-checked by the screen it opens.
                var payload = group.Select(i => new
                {
                    id = i.Id,
                    typeKey = i.TypeKey,
                    title = i.Title,
                    body = i.Body,
                    branchName = i.BranchName,
                    occurredAtUtc = i.OccurredAtUtc,
                    wasOffline = i.WasOffline,
                    minutes = i.Minutes,
                    url = i.Url,
                    eventId = i.EventId,
                }).ToList();
                await _hub.Clients.Clients(to).SendAsync(NotificationsHub.Event, new { items = payload }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Notifications: live send to {Recipient} failed", group.Key);
            }
        }
    }
}

/// <summary>
/// Short-lived tickets for the live channel. Issued to an authenticated session (its JWT has been checked — token
/// version, active account, active company — by the ordinary request pipeline), valid for <see cref="Lifetime"/>, and
/// mapped to that (company, person) only. One backend process, so an in-memory store is enough; a second replica would
/// need a shared one (and a SignalR backplane) anyway.
/// </summary>
public sealed class HubTickets
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private readonly IMemoryCache _cache;

    public HubTickets(IMemoryCache cache) => _cache = cache;

    /// <summary><paramref name="tokenVersion"/>: the version of the session asking — the JWT check has just proven it
    /// current. The connection is registered under it, and closed the moment it stops being current.</summary>
    public string Issue(Guid tenantId, Guid employeeId, int tokenVersion)
    {
        var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _cache.Set(Key(ticket), (tenantId, employeeId, tokenVersion), Lifetime);
        return ticket;
    }

    public bool TryRead(string? ticket, out Guid tenantId, out Guid employeeId, out int tokenVersion)
    {
        tenantId = employeeId = Guid.Empty;
        tokenVersion = 0;
        if (string.IsNullOrEmpty(ticket) || ticket.Length > 100
            || !_cache.TryGetValue(Key(ticket), out (Guid Tenant, Guid Employee, int Version) owner))
            return false;
        (tenantId, employeeId, tokenVersion) = owner;
        return true;
    }

    private static string Key(string ticket) => $"hub-ticket:{ticket}";
}

/// <summary>Authenticates <see cref="NotificationsHub"/> requests by a <see cref="HubTickets"/> ticket, from the
/// SignalR client's Authorization header (negotiate) or its access_token query (the WebSocket).</summary>
public sealed class HubTicketAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "HubTicket";

    /// <summary>
    /// How long one live connection may stay open before the server closes it (MapHub's CloseOnAuthenticationExpiration)
    /// and the client comes back with a new ticket, which re-runs the whole JWT check. A backstop only: a session that
    /// ends is cut off at once by <see cref="LiveConnections"/> — nothing more is sent to it, and the sweep closes it within
    /// seconds — so this is not how long a revoked session keeps listening.
    /// </summary>
    public static TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(60);
    private readonly HubTickets _tickets;

    public HubTicketAuthentication(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, HubTickets tickets)
        : base(options, logger, encoder) => _tickets = tickets;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? ticket = Request.Query["access_token"];
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(ticket) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            ticket = header["Bearer ".Length..].Trim();
        if (!_tickets.TryRead(ticket, out var tenantId, out var employeeId, out var tokenVersion))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [
                new Claim("sub", employeeId.ToString()),
                new Claim("tid", tenantId.ToString()),
                new Claim("tv", tokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            SchemeName, "sub", "role");
        var properties = new AuthenticationProperties { ExpiresUtc = TimeProvider.GetUtcNow() + ConnectionLifetime };
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), properties, SchemeName)));
    }
}

/// <summary>«This company has fresh events» — so the worker runs now rather than at its next poll.</summary>
public interface INotificationHint
{
    void Ready(Guid tenantId);
    ChannelReader<Guid> Reader { get; }
}

public sealed class NotificationHint : INotificationHint
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Ready(Guid tenantId) => _channel.Writer.TryWrite(tenantId);
    public ChannelReader<Guid> Reader => _channel.Reader;
}
