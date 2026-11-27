using AttendanceQR.Api.Jobs;
using AttendanceQR.Api.Notifications;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The live channel ends with the session. A connection is registered under the session it was opened in — company,
/// person, token version — and every send reads the sessions first, so a PIN reset, «hər yerdən çıxış», a deactivation or
/// a suspended company stops the pop-ups at once; the sweep closes the connection itself within seconds. Nothing waits
/// for the connection to expire.
/// </summary>
public class NotificationLiveSessionTests
{
    private static LiveNotification For(Guid recipient) =>
        new(recipient, Guid.NewGuid(), "attendance.checkin", "İşçi giriş etdi", "Əliyev Elvin işə gəldi", "B1",
            DateTime.UtcNow, false, null, "/admin/notifications");

    private static void Bump(AccessWorld w, Guid who)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Employees.Single(e => e.Id == who).TokenVersion++;
        db.SaveChanges();
    }

    private static void Deactivate(AccessWorld w, Guid who)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Employees.Single(e => e.Id == who).IsActive = false;
        db.SaveChanges();
    }

    private static void Suspend(AccessWorld w, Guid tenantId)
    {
        using var db = w.Context(tenantId);
        db.Tenants.Single(t => t.Id == tenantId).IsActive = false;
        db.SaveChanges();
    }

    private static async Task<IReadOnlyDictionary<Guid, int>> Sessions(AccessWorld w, Guid tenantId, params Guid[] people)
    {
        using var db = w.Context(tenantId);
        return await LiveSessions.ReadAsync(db, tenantId, people, CancellationToken.None);
    }

    [Fact]
    public async Task Sessions_are_read_from_the_account_and_the_company_as_they_are_now()
    {
        using var w = new AccessWorld();
        Bump(w, w.M1);
        Deactivate(w, w.M2);

        var a = await Sessions(w, AccessWorld.TenantA, w.Admin1, w.M1, w.M2, w.OtherCompanyManager);

        Assert.Equal(0, a[w.Admin1]);
        Assert.Equal(1, a[w.M1]);                         // the version a PIN reset left
        Assert.False(a.ContainsKey(w.M2));                // deactivated: no session at all
        Assert.False(a.ContainsKey(w.OtherCompanyManager)); // another company's person is never found in this one

        Suspend(w, AccessWorld.TenantA);
        Assert.Empty(await Sessions(w, AccessWorld.TenantA, w.Admin1, w.M1));
    }

    [Fact]
    public async Task A_PIN_reset_cuts_off_the_open_connection_before_the_next_notification_reaches_it()
    {
        using var w = new AccessWorld();
        var connections = new LiveConnections();
        var aborted = new List<string>();
        connections.Add("m1-old-phone", new(AccessWorld.TenantA, w.M1, 0), () => aborted.Add("m1-old-phone"));
        connections.Add("m2-panel", new(AccessWorld.TenantA, w.M2, 0), () => aborted.Add("m2-panel"));
        var hub = new RecordingHub();
        var live = new SignalRNotificationLive(hub, connections, NullLogger<SignalRNotificationLive>.Instance);

        Bump(w, w.M1);   // the admin resets M1's PIN; M1's old phone still has the panel open
        await live.SendAsync(AccessWorld.TenantA, [For(w.M1), For(w.M2)], await Sessions(w, AccessWorld.TenantA, w.M1, w.M2));

        Assert.Equal(["m2-panel"], hub.Sent.Select(s => s.To).Single());
        Assert.Equal(["m1-old-phone"], aborted);
        Assert.Equal(1, connections.Count);

        // M1 signs in again with the new PIN: the new session hears its notifications.
        connections.Add("m1-new", new(AccessWorld.TenantA, w.M1, 1), () => aborted.Add("m1-new"));
        hub.Sent.Clear();
        await live.SendAsync(AccessWorld.TenantA, [For(w.M1)], await Sessions(w, AccessWorld.TenantA, w.M1));
        Assert.Equal(["m1-new"], hub.Sent.Single().To);
    }

    [Fact]
    public async Task A_deactivated_account_or_a_suspended_company_hears_nothing_more()
    {
        using var w = new AccessWorld();
        var connections = new LiveConnections();
        var aborted = new List<string>();
        connections.Add("m2", new(AccessWorld.TenantA, w.M2, 0), () => aborted.Add("m2"));
        connections.Add("admin", new(AccessWorld.TenantA, w.Admin1, 0), () => aborted.Add("admin"));
        var hub = new RecordingHub();
        var live = new SignalRNotificationLive(hub, connections, NullLogger<SignalRNotificationLive>.Instance);

        Deactivate(w, w.M2);
        await live.SendAsync(AccessWorld.TenantA, [For(w.M2), For(w.Admin1)], await Sessions(w, AccessWorld.TenantA, w.M2, w.Admin1));
        Assert.Equal(["admin"], hub.Sent.Single().To);
        Assert.Equal(["m2"], aborted);

        Suspend(w, AccessWorld.TenantA);
        hub.Sent.Clear();
        await live.SendAsync(AccessWorld.TenantA, [For(w.Admin1)], await Sessions(w, AccessWorld.TenantA, w.Admin1));
        Assert.Empty(hub.Sent);
        Assert.Equal(["m2", "admin"], aborted);
    }

    [Fact]
    public async Task The_sweep_closes_ended_sessions_without_waiting_for_a_notification_and_only_in_their_own_company()
    {
        using var w = new AccessWorld();
        var connections = new LiveConnections();
        var aborted = new List<string>();
        void Open(string id, Guid tenant, Guid who, int version = 0) =>
            connections.Add(id, new(tenant, who, version), () => aborted.Add(id));
        Open("admin", AccessWorld.TenantA, w.Admin1);
        Open("m1-reset", AccessWorld.TenantA, w.M1);
        Open("m2-deactivated", AccessWorld.TenantA, w.M2);
        Open("b-manager", AccessWorld.TenantB, w.OtherCompanyManager);
        // A session that claims company A for a person of company B: the tenant filter never finds them in A.
        Open("crossed", AccessWorld.TenantA, w.OtherCompanyManager);
        Bump(w, w.M1);
        Deactivate(w, w.M2);
        var worker = Worker(w, connections);

        Assert.Equal(3, await worker.CloseEndedSessionsAsync(CancellationToken.None));
        Assert.Equal(["crossed", "m1-reset", "m2-deactivated"], aborted.Order());
        Assert.Equal(2, connections.Count);

        Suspend(w, AccessWorld.TenantB);
        Assert.Equal(1, await worker.CloseEndedSessionsAsync(CancellationToken.None));
        Assert.Contains("b-manager", aborted);
        Assert.DoesNotContain("admin", aborted);
        Assert.Equal(0, await worker.CloseEndedSessionsAsync(CancellationToken.None));
    }

    [Fact]
    public void A_connection_is_heard_only_by_its_own_person_in_its_own_company()
    {
        var connections = new LiveConnections();
        var person = Guid.NewGuid();
        connections.Add("a", new(AccessWorld.TenantA, person, 2), () => { });
        connections.Add("b", new(AccessWorld.TenantB, person, 2), () => { });
        connections.Add("other", new(AccessWorld.TenantA, Guid.NewGuid(), 2), () => { });

        Assert.Equal(["a"], connections.Current(AccessWorld.TenantA, person, 2));
        Assert.Equal(["b"], connections.Current(AccessWorld.TenantB, person, 2));
        Assert.Equal(3, connections.Count);
    }

    private static NotificationWorker Worker(AccessWorld w, LiveConnections connections)
    {
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp => w.Context(sp.GetRequiredService<ITenantContext>()));
        var provider = services.BuildServiceProvider();
        return new NotificationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), new NotificationScanOutboxTests.FakeHint(),
            new SignalRNotificationLive(new RecordingHub(), connections, NullLogger<SignalRNotificationLive>.Instance),
            connections, w.Options, new PushOptions(), new FcmOptions(), NullLogger<NotificationWorker>.Instance);
    }

    /// <summary>Records what is sent to which connections; any other way of addressing clients is a test failure.</summary>
    private sealed class RecordingHub : IHubContext<NotificationsHub>, IHubClients, IClientProxy
    {
        private IReadOnlyList<string> _to = [];
        public List<(IReadOnlyList<string> To, string Method)> Sent { get; } = [];

        IHubClients IHubContext<NotificationsHub>.Clients => this;
        IGroupManager IHubContext<NotificationsHub>.Groups => throw new NotSupportedException();

        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds)
        {
            _to = connectionIds.ToList();
            return this;
        }

        IClientProxy IHubClients<IClientProxy>.All => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Group(string groupName) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.User(string userId) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((_to, method));
            return Task.CompletedTask;
        }
    }
}
