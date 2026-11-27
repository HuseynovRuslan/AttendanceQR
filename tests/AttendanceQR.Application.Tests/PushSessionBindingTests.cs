using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.Jobs;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A push subscription follows the session that registered it. A TokenVersion bump (PIN reset, «hər yerdən
/// çıxış», …) ends the session on every device and the device's pushes with it; signing in again there
/// re-registers it. Decided at send time (PushSessionBinding), so none of the fifteen places that bump the
/// version has to remember anything.
/// </summary>
public class PushSessionBindingTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000005a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000005b2");

    [Fact]
    public async Task A_device_stops_receiving_once_its_session_ends_and_its_row_is_pruned()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana");
        Assert.IsType<OkObjectResult>(await w.Push(TenantA, ana).Subscribe(Sub("https://push.example/ana")));

        Assert.Equal(1, await w.Notify(TenantA, ana));
        Assert.Equal(["https://push.example/ana"], w.Web.Sent);

        w.EndSessions(ana);   // the PIN was reset

        Assert.Equal(0, await w.Notify(TenantA, ana));
        Assert.Single(w.Web.Sent);                        // nothing more went out
        Assert.Empty(w.Rows());                            // and the dead binding is gone
    }

    [Fact]
    public async Task Signing_in_again_re_binds_the_device_and_its_pushes_come_back()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana");
        await w.Push(TenantA, ana).Subscribe(Sub("https://push.example/ana"));
        w.EndSessions(ana);

        // The app, under the new session's token, re-registers the subscription the browser still holds.
        Assert.IsType<OkObjectResult>(await w.Push(TenantA, ana).Subscribe(Sub("https://push.example/ana", refresh: true)));

        var row = Assert.Single(w.Rows());
        Assert.Equal(1, row.TokenVersion);
        Assert.Equal(1, await w.Notify(TenantA, ana));
    }

    [Fact]
    public async Task A_refresh_never_takes_another_accounts_endpoint_and_an_explicit_enable_still_does()
    {
        // A crew phone: one browser, several accounts. Switching accounts makes the app re-register — that must
        // not take the first account's check-out reminder away. Tapping «aktiv et» moves it, as it always has.
        using var w = new World();
        var first = w.Person(TenantA, "Birinci");
        var second = w.Person(TenantA, "İkinci", tokenVersion: 4);
        await w.Push(TenantA, first).Subscribe(Sub("https://push.example/crew"));

        var refreshed = Assert.IsType<OkObjectResult>(
            await w.Push(TenantA, second).Subscribe(Sub("https://push.example/crew", refresh: true)));
        Assert.Contains("bound = False", refreshed.Value!.ToString());
        Assert.Equal(first, Assert.Single(w.Rows()).EmployeeId);
        Assert.Equal(1, await w.Notify(TenantA, first));
        Assert.Equal(0, await w.Notify(TenantA, second));

        await w.Push(TenantA, second).Subscribe(Sub("https://push.example/crew"));
        var row = Assert.Single(w.Rows());
        Assert.Equal((second, 4), (row.EmployeeId, row.TokenVersion));
        Assert.Equal(1, await w.Notify(TenantA, second));
    }

    [Fact]
    public async Task A_refresh_for_a_subscription_the_server_forgot_registers_it_for_the_caller()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana", tokenVersion: 2);

        var ok = Assert.IsType<OkObjectResult>(await w.Push(TenantA, ana).Subscribe(Sub("https://push.example/new", refresh: true)));
        Assert.Contains("bound = True", ok.Value!.ToString());
        var row = Assert.Single(w.Rows());
        Assert.Equal((ana, 2), (row.EmployeeId, row.TokenVersion));
    }

    [Fact]
    public async Task The_native_app_registration_follows_the_session_too()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana");
        await w.Push(TenantA, ana).RegisterNative(new PushRegisterNativeRequest("fcm-token-1"));

        Assert.Equal(1, await w.Notify(TenantA, ana));
        w.EndSessions(ana);
        Assert.Equal(0, await w.Notify(TenantA, ana));
        Assert.Equal(["fcm-token-1"], w.Fcm.Sent);
        Assert.Empty(w.Rows());

        // Signed in again: the app registers its token, and the next session ends before any push — the
        // re-registration under the session after that re-binds the row that is still there.
        await w.Push(TenantA, ana).RegisterNative(new PushRegisterNativeRequest("fcm-token-1"));
        w.EndSessions(ana);
        await w.Push(TenantA, ana).RegisterNative(new PushRegisterNativeRequest("fcm-token-1"));
        Assert.Equal(2, Assert.Single(w.Rows()).TokenVersion);
        Assert.Equal(1, await w.Notify(TenantA, ana));
    }

    [Fact]
    public async Task A_row_from_before_the_binding_is_adopted_at_send_time_and_then_obeys_it()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana", tokenVersion: 5);
        w.UnboundRow(TenantA, ana, "https://push.example/old");

        Assert.Equal(1, await w.Notify(TenantA, ana));       // still reaches them: nobody lost a reminder
        Assert.Equal(5, Assert.Single(w.Rows()).TokenVersion);

        w.EndSessions(ana);
        Assert.Equal(0, await w.Notify(TenantA, ana));
        Assert.Empty(w.Rows());
    }

    [Fact]
    public async Task Startup_binds_every_companys_earlier_rows_each_to_its_own_employees_version()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana", tokenVersion: 2);
        var bahar = w.Person(TenantB, "Bahar", tokenVersion: 7);
        w.UnboundRow(TenantA, ana, "https://push.example/a");
        w.UnboundRow(TenantB, bahar, "https://push.example/b");
        // A row of company A pointing at a person who exists only in company B: A's scope cannot see that
        // person, so the adoption must not either — the tenant filter holds inside the job.
        w.UnboundRow(TenantA, bahar, "https://push.example/a-to-b");
        var job = new PushSubscriptionAdoption(w.Scopes, NullLogger<PushSubscriptionAdoption>.Instance);

        Assert.Equal(2, await job.AdoptAllAsync(default));

        var rows = w.Rows().ToDictionary(r => r.Endpoint, r => (r.TenantId, r.TokenVersion));
        Assert.Equal((TenantA, 2), rows["https://push.example/a"]);
        Assert.Equal((TenantB, 7), rows["https://push.example/b"]);
        Assert.Equal((TenantA, PushSubscription.Unbound), rows["https://push.example/a-to-b"]);
        Assert.Equal(0, await w.Notify(TenantA, bahar));      // and nothing in A ever sends to B's person

        Assert.Equal(0, await job.AdoptAllAsync(default));    // idempotent: the next start finds nothing
    }

    [Fact]
    public async Task One_ended_session_does_not_cost_the_other_people_in_the_same_send_their_push()
    {
        using var w = new World();
        var ana = w.Person(TenantA, "Ana");
        var bahar = w.Person(TenantA, "Bahar");
        await w.Push(TenantA, ana).Subscribe(Sub("https://push.example/ana"));
        await w.Push(TenantA, bahar).Subscribe(Sub("https://push.example/bahar"));
        w.EndSessions(ana);

        Assert.Equal(1, await w.NotifyMany(TenantA, ana, bahar));
        Assert.Equal(["https://push.example/bahar"], w.Web.Sent);
        Assert.Equal(bahar, Assert.Single(w.Rows()).EmployeeId);
    }

    [Fact]
    public async Task Subscribing_without_an_account_row_is_refused()
    {
        using var w = new World();
        var ghost = Guid.NewGuid();
        Assert.IsType<UnauthorizedResult>(await w.Push(TenantA, ghost).Subscribe(Sub("https://push.example/x")));
        Assert.Empty(w.Rows());
    }

    [Fact]
    public async Task A_support_session_never_creates_refreshes_or_takes_over_a_subscription()
    {
        using var w = new World();
        var admin = w.Person(TenantA, "Admin");
        var operatorId = Guid.NewGuid();

        // An explicit enable, the app's automatic re-bind, a native registration: refused, nothing written.
        foreach (var refresh in new[] { false, true })
        {
            var refused = await w.Push(TenantA, admin, operatorId).Subscribe(Sub("https://push.example/operator", refresh));
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(refused).StatusCode);
        }
        var native = await w.Push(TenantA, admin, operatorId).RegisterNative(new PushRegisterNativeRequest("operator-token"));
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(native).StatusCode);
        Assert.Empty(w.Rows());

        // The admin's own phone, subscribed by the admin, is not taken over by the support session's re-bind either.
        await w.Push(TenantA, admin).Subscribe(Sub("https://push.example/admin-phone"));
        await w.Push(TenantA, admin, operatorId).Subscribe(Sub("https://push.example/admin-phone", refresh: true));
        var row = Assert.Single(w.Rows());
        Assert.Equal(admin, row.EmployeeId);
        Assert.Equal(1, await w.Notify(TenantA, admin));
    }

    private static PushSubscribeRequest Sub(string endpoint, bool refresh = false) =>
        new(endpoint, "p256dh-key", "auth-secret", refresh);

    // --- the world ------------------------------------------------------------------------------------

    private sealed class RecordingWeb : IPushSender
    {
        public List<string> Sent { get; } = new();
        public Task<PushSendResult> SendAsync(string endpoint, string p256dh, string auth, string title, string body, string? url, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add(endpoint);
            return Task.FromResult(PushSendResult.Delivered);
        }
    }

    private sealed class RecordingFcm : IFcmSender
    {
        public bool IsConfigured => true;
        public List<string> Sent { get; } = new();
        public Task<PushSendResult> SendAsync(string token, string title, string body, string? url, CancellationToken ct = default)
        {
            lock (Sent) Sent.Add(token);
            return Task.FromResult(PushSendResult.Delivered);
        }
    }

    private sealed class World : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly List<IServiceScope> _scopes = new();
        public RecordingWeb Web { get; } = new();
        public RecordingFcm Fcm { get; } = new();
        public IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();

        public World()
        {
            var name = $"push-session-{Guid.NewGuid()}";
            var services = new ServiceCollection();
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped(sp => new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options,
                sp.GetRequiredService<ITenantContext>()));
            _services = services.BuildServiceProvider();

            using var db = Context(TenantA);
            db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = false });
            db.SaveChanges();
        }

        public AppDbContext Context(Guid tenantId)
        {
            var scope = _services.CreateScope();
            _scopes.Add(scope);
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Resolve(tenantId);
            return scope.ServiceProvider.GetRequiredService<AppDbContext>();
        }

        public Guid Person(Guid tenantId, string name, int tokenVersion = 0)
        {
            var id = Guid.NewGuid();
            using var db = Context(tenantId);
            db.Employees.Add(new Employee
            {
                Id = id, TenantId = tenantId, FullName = name, Email = $"{id:N}@t.local", LocationId = Guid.NewGuid(),
                Role = EmployeeRole.Employee, IsActive = true, PasswordHash = "x", TokenVersion = tokenVersion,
            });
            db.SaveChanges();
            return id;
        }

        /// <summary>What a PIN reset, «hər yerdən çıxış» or any other TokenVersion bump does.</summary>
        public void EndSessions(Guid employeeId)
        {
            using var db = Context(TenantA);
            db.Employees.IgnoreQueryFilters().Single(e => e.Id == employeeId).TokenVersion++;
            db.SaveChanges();
        }

        /// <summary>A row registered before subscriptions followed the session (the migration's -1).</summary>
        public void UnboundRow(Guid tenantId, Guid employeeId, string endpoint)
        {
            using var db = Context(tenantId);
            db.PushSubscriptions.Add(new PushSubscription
            {
                TenantId = tenantId, EmployeeId = employeeId, Endpoint = endpoint, P256dh = "k", Auth = "a",
            });
            db.SaveChanges();
            Assert.Equal(PushSubscription.Unbound, db.PushSubscriptions.AsNoTracking().Single(s => s.Endpoint == endpoint).TokenVersion);
        }

        public List<PushSubscription> Rows()
        {
            using var db = Context(TenantA);
            return db.PushSubscriptions.IgnoreQueryFilters().AsNoTracking().ToList();
        }

        /// <summary>The push endpoints as the signed-in <paramref name="employeeId"/> reaches them — or, with
        /// <paramref name="operatorId"/>, as a support session borrowing that account («imp» claim).</summary>
        public PushController Push(Guid tenantId, Guid employeeId, Guid? operatorId = null)
        {
            var db = Context(tenantId);
            var claims = new List<Claim> { new("sub", employeeId.ToString()), new("role", nameof(EmployeeRole.Employee)) };
            if (operatorId is { } op)
                claims.Add(new Claim("imp", op.ToString()));
            return new PushController(db, new PushOptions { PublicKey = "pub", PrivateKey = "priv" }, NotifierFor(db))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
                },
            };
        }

        private PushNotifier NotifierFor(AppDbContext db) =>
            new(db, Web, Fcm, NullLogger<PushNotifier>.Instance);

        public Task<int> Notify(Guid tenantId, Guid employeeId) => NotifyMany(tenantId, employeeId);

        public async Task<int> NotifyMany(Guid tenantId, params Guid[] employeeIds)
        {
            using var db = Context(tenantId);
            return await NotifierFor(db).NotifyEmployeesAsync(employeeIds, "t", "b", "/home");
        }

        public void Dispose()
        {
            foreach (var s in _scopes) s.Dispose();
            _services.Dispose();
        }
    }
}
