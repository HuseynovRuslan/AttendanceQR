using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The branch manager hears about somebody stuck at the poster, or asking for a new phone.
///
/// On 03.10.2026 sixteen people at Bakı Abadlıq were refused by their own phones and stayed absent, most
/// for a location permission a manager standing beside them could have switched on in a minute, and a
/// new-phone request waited over twenty hours for a manager who could approve it and never knew. These
/// pin who is told, when, how often — and that a person who sorted it out alone is not reported at all.
/// </summary>
public class StaffAlertTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    /// <summary>08:10 in Baku, 03.10.2026.</summary>
    private static readonly DateTime Now = new(2026, 10, 3, 4, 10, 0, DateTimeKind.Utc);

    // --- what is worth an alert, and when -----------------------------------

    [Theory]
    [InlineData("GpsPermissionDenied")]
    [InlineData("GpsUnavailable")]
    [InlineData("CameraDenied")]
    [InlineData("CameraNotFound")]
    [InlineData("DeviceBindLimit")]
    [InlineData("DeviceRevoked")]
    [InlineData("SharedDeviceNotAllowed")]
    [InlineData(StaffAlertKinds.DeviceChangeRequested)]
    public void A_problem_the_manager_can_fix_is_alerted(string kind) =>
        Assert.True(StaffAlertKinds.IsAlertable(kind));

    [Theory]
    [InlineData("GpsTimeout")]       // a weak signal clears on its own
    [InlineData("GpsInaccurate")]    // a warning, never a refusal
    [InlineData("CameraInUse")]      // another app, gone in a moment
    [InlineData("OutsideRadius")]    // mostly somebody scanning from the bus
    [InlineData("TooSoonToCheckOut")]
    public void Noise_is_not(string kind) => Assert.False(StaffAlertKinds.IsAlertable(kind));

    [Fact]
    public void A_refusal_waits_out_the_grace_period_and_a_request_goes_at_once()
    {
        Assert.Equal(Now.AddMinutes(10), StaffAlertKinds.DueAtUtc(new StaffAlert(TenantId, Guid.NewGuid(), "GpsPermissionDenied", Now), Baku));
        Assert.Equal(Now, StaffAlertKinds.DueAtUtc(new StaffAlert(TenantId, Guid.NewGuid(), StaffAlertKinds.DeviceChangeRequested, Now), Baku));
    }

    /// <summary>A moment on 03.10.2026, Baku time, as UTC (Baku is UTC+4 all year).</summary>
    private static DateTime BakuTime(int day, int hour, int minute) =>
        new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Utc).AddHours(-4);

    [Theory]
    // The 04.10.2026 push that went at 04:07 — now it waits for the morning.
    [InlineData(3, 4, 17, 3, 6, 30)]
    // Late evening waits for the NEXT morning.
    [InlineData(3, 22, 0, 4, 6, 30)]
    [InlineData(3, 23, 50, 4, 6, 30)]
    [InlineData(3, 6, 29, 3, 6, 30)]
    // The day is untouched, to the minute at both ends.
    [InlineData(3, 6, 30, 3, 6, 30)]
    [InlineData(3, 21, 59, 3, 21, 59)]
    [InlineData(3, 12, 0, 3, 12, 0)]
    public void Nothing_goes_out_in_the_night(int day, int hour, int minute, int dueDay, int dueHour, int dueMinute)
    {
        Assert.Equal(BakuTime(dueDay, dueHour, dueMinute), StaffAlertKinds.AfterQuietHours(BakuTime(day, hour, minute), Baku));
    }

    [Fact]
    public void A_refusal_at_the_edge_of_the_night_is_held_too()
    {
        // Refused at 21:55: the grace period runs out at 22:05, inside quiet hours.
        var alert = new StaffAlert(TenantId, Guid.NewGuid(), "GpsPermissionDenied", BakuTime(3, 21, 55));
        Assert.Equal(BakuTime(4, 6, 30), StaffAlertKinds.DueAtUtc(alert, Baku));

        var request = new StaffAlert(TenantId, Guid.NewGuid(), StaffAlertKinds.DeviceChangeRequested, BakuTime(3, 23, 10));
        Assert.Equal(BakuTime(4, 6, 30), StaffAlertKinds.DueAtUtc(request, Baku));
    }

    [Fact]
    public void The_push_names_the_person_the_problem_and_the_fix()
    {
        var push = StaffAlertKinds.Describe("GpsPermissionDenied", "Bağırov Zamiq", TimeSpan.FromMinutes(10.2))!.Value;

        Assert.Equal("Bağırov Zamiq skan edə bilmir", push.Title);
        Assert.Contains("10 dəqiqədir", push.Body);
        Assert.Contains("məkan (GPS) icazəsi", push.Body);
        Assert.Contains("kömək edin", push.Body);
        Assert.Equal("/admin/problems", push.Url);

        Assert.Equal("/admin/device-changes", StaffAlertKinds.Describe(StaffAlertKinds.DeviceChangeRequested, "X", TimeSpan.Zero)!.Value.Url);
        Assert.Null(StaffAlertKinds.Describe("OutsideRadius", "X", TimeSpan.FromMinutes(10)));
    }

    [Theory]
    [InlineData(10.0, "10 dəqiqədir")]
    [InlineData(23.7, "23 dəqiqədir")]
    // Held from 04:07 to 06:30 — the morning push must not say ten minutes.
    [InlineData(143.0, "2 saatdır")]
    [InlineData(510.0, "8 saatdır")]
    public void The_push_says_how_long_it_has_really_been(double minutes, string since)
    {
        var push = StaffAlertKinds.Describe("CameraDenied", "X", TimeSpan.FromMinutes(minutes))!.Value;

        Assert.StartsWith($"{since} alınmır:", push.Body);
    }

    // --- who hears it ---------------------------------------------------------

    private sealed class FakeNotifier(int reached = -1) : IPushNotifier
    {
        public List<(Guid[] To, string Title, string Body, string? Url)> Sent { get; } = [];
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> employeeIds, string title, string body, string? url, CancellationToken ct = default)
        {
            Sent.Add((employeeIds.ToArray(), title, body, url));
            return Task.FromResult(reached < 0 ? employeeIds.Count : reached);
        }
    }

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid SiteA { get; } = Guid.NewGuid();
        public Guid SiteB { get; } = Guid.NewGuid();
        public Guid Unmanaged { get; } = Guid.NewGuid();
        public Guid Worker { get; } = Guid.NewGuid();
        public Guid ManagerA { get; } = Guid.NewGuid();
        public Guid ManagerB { get; } = Guid.NewGuid();
        public Guid Admin { get; } = Guid.NewGuid();

        public World()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"staff-alerts-{Guid.NewGuid()}").Options, tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            foreach (var (id, name) in new[] { (SiteA, "Akkord"), (SiteB, "Bazar qabağı"), (Unmanaged, "Kanal") })
                Db.Locations.Add(new Location { Id = id, TenantId = TenantId, Name = name, IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
            Person(Worker, "Bağırov Zamiq", SiteA, EmployeeRole.Employee);
            Person(ManagerA, "Akkord Meneceri", SiteA, EmployeeRole.Manager);
            Person(ManagerB, "Bazar Meneceri", SiteB, EmployeeRole.Manager);
            Person(Admin, "Admin", SiteB, EmployeeRole.Admin);
            Db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantId, EmployeeId = ManagerA, LocationId = SiteA });
            Db.ManagedLocations.Add(new ManagedLocation { TenantId = TenantId, EmployeeId = ManagerB, LocationId = SiteB });
            Db.SaveChanges();
        }

        public void Person(Guid id, string name, Guid site, EmployeeRole role)
        {
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = name, Email = $"{id:N}@t.local", LocationId = site,
                Role = role, IsActive = true, PasswordHash = "x",
            });
            Db.SaveChanges();
        }

        public void Audit(Guid employeeId, AuditEventType type, DateTime at, string? reason = null)
        {
            Db.AuditLogs.Add(new AuditLog { TenantId = TenantId, EmployeeId = employeeId, EventType = type, Reason = reason, CreatedAtUtc = at });
            Db.SaveChanges();
        }

        public void Request(Guid employeeId, DeviceChangeStatus status)
        {
            Db.DeviceChangeRequests.Add(new DeviceChangeRequest
            {
                TenantId = TenantId, EmployeeId = employeeId, NewDeviceFingerprint = "fp", Status = status, RequestedAtUtc = Now,
            });
            Db.SaveChanges();
        }

        public Task<StaffAlertOutcome> Deliver(IPushNotifier notifier, Guid employeeId, string kind, DateTime raisedAt, DateTime? now = null) =>
            StaffAlertDispatcher.HandleAsync(Db, notifier, new StaffAlert(TenantId, employeeId, kind, raisedAt), now ?? raisedAt.AddMinutes(10), Baku);

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task The_managers_of_the_persons_own_branch_hear_about_it_and_nobody_else()
    {
        using var w = new World();
        var push = new FakeNotifier();

        var outcome = await w.Deliver(push, w.Worker, "GpsPermissionDenied", Now);

        Assert.Equal(StaffAlertOutcome.Sent, outcome);
        var sent = Assert.Single(push.Sent);
        Assert.Equal([w.ManagerA], sent.To);
        Assert.Equal("Bağırov Zamiq skan edə bilmir", sent.Title);
        var record = await w.Db.AuditLogs.SingleAsync(a => a.EventType == AuditEventType.StaffAlertSent);
        Assert.Equal(w.Worker, record.EmployeeId);
        Assert.Equal("GpsPermissionDenied|1/1 menecer", record.Reason);
    }

    [Fact]
    public async Task A_branch_without_a_manager_falls_back_to_the_admins()
    {
        using var w = new World();
        var loner = Guid.NewGuid();
        w.Person(loner, "Kanal İşçisi", w.Unmanaged, EmployeeRole.Employee);
        var push = new FakeNotifier();

        await w.Deliver(push, loner, "CameraDenied", Now);

        Assert.Equal([w.Admin], Assert.Single(push.Sent).To);
    }

    [Fact]
    public async Task A_manager_stuck_themselves_is_not_told_about_themselves()
    {
        using var w = new World();
        var push = new FakeNotifier();

        await w.Deliver(push, w.ManagerA, "GpsPermissionDenied", Now);

        // The only manager of that branch IS the person, so it goes up to the admins instead.
        Assert.Equal([w.Admin], Assert.Single(push.Sent).To);
    }

    [Fact]
    public async Task Nobody_hears_about_somebody_who_got_in_meanwhile()
    {
        using var w = new World();
        w.Audit(w.Worker, AuditEventType.CheckInSuccess, Now.AddMinutes(3));
        var push = new FakeNotifier();

        var outcome = await w.Deliver(push, w.Worker, "GpsPermissionDenied", Now);

        Assert.Equal(StaffAlertOutcome.Recovered, outcome);
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task A_request_is_sent_even_if_the_person_is_scanning_with_another_phone()
    {
        using var w = new World();
        w.Request(w.Worker, DeviceChangeStatus.Pending);
        w.Audit(w.Worker, AuditEventType.CheckInSuccess, Now.AddMinutes(1));
        var push = new FakeNotifier();

        Assert.Equal(StaffAlertOutcome.Sent, await w.Deliver(push, w.Worker, StaffAlertKinds.DeviceChangeRequested, Now, Now));
    }

    [Theory]
    [InlineData(DeviceChangeStatus.Approved)]
    [InlineData(DeviceChangeStatus.Rejected)]
    public async Task A_request_somebody_answered_overnight_is_not_announced_in_the_morning(DeviceChangeStatus status)
    {
        // Asked at 23:10, held until 06:30 — and an admin who was still up dealt with it at 23:30.
        using var w = new World();
        w.Request(w.Worker, status);
        var push = new FakeNotifier();

        var outcome = await w.Deliver(push, w.Worker, StaffAlertKinds.DeviceChangeRequested, BakuTime(3, 23, 10), BakuTime(4, 6, 30));

        Assert.Equal(StaffAlertOutcome.AlreadyHandled, outcome);
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task A_refusal_held_overnight_goes_in_the_morning_with_the_real_wait()
    {
        using var w = new World();
        var push = new FakeNotifier();

        var outcome = await w.Deliver(push, w.Worker, "GpsPermissionDenied", BakuTime(4, 4, 7), BakuTime(4, 6, 30));

        Assert.Equal(StaffAlertOutcome.Sent, outcome);
        Assert.StartsWith("2 saatdır alınmır:", Assert.Single(push.Sent).Body);
    }

    [Fact]
    public async Task A_night_shift_worker_who_got_in_before_morning_is_never_reported()
    {
        using var w = new World();
        w.Audit(w.Worker, AuditEventType.CheckInSuccess, BakuTime(4, 5, 0));
        var push = new FakeNotifier();

        var outcome = await w.Deliver(push, w.Worker, "GpsPermissionDenied", BakuTime(4, 4, 7), BakuTime(4, 6, 30));

        Assert.Equal(StaffAlertOutcome.Recovered, outcome);
        Assert.Empty(push.Sent);
    }

    [Fact]
    public async Task Once_a_day_per_person_and_problem()
    {
        // Bayramov Musa was refused 28 times on 03.10.2026; his manager should hear about it once.
        using var w = new World();
        var push = new FakeNotifier();

        Assert.Equal(StaffAlertOutcome.Sent, await w.Deliver(push, w.Worker, "GpsPermissionDenied", Now));
        Assert.Equal(StaffAlertOutcome.AlreadyAlerted, await w.Deliver(push, w.Worker, "GpsPermissionDenied", Now.AddHours(2)));
        Assert.Equal(StaffAlertOutcome.Sent, await w.Deliver(push, w.Worker, "CameraDenied", Now.AddHours(2)));
        Assert.Equal(2, push.Sent.Count);
    }

    [Fact]
    public async Task The_next_day_counts_again_from_Baku_midnight()
    {
        using var w = new World();
        // Said at 23:30 Baku on 02.10 — that is 19:30 UTC, already «yesterday» on the company's calendar.
        w.Audit(w.Worker, AuditEventType.StaffAlertSent, new DateTime(2026, 10, 2, 19, 30, 0, DateTimeKind.Utc), "GpsPermissionDenied|1/1 menecer");
        var push = new FakeNotifier();

        Assert.Equal(StaffAlertOutcome.Sent, await w.Deliver(push, w.Worker, "GpsPermissionDenied", Now));
    }

    [Fact]
    public async Task It_is_recorded_even_when_no_manager_has_notifications_on()
    {
        using var w = new World();
        var push = new FakeNotifier(reached: 0);

        await w.Deliver(push, w.Worker, "DeviceBindLimit", Now);

        Assert.Equal("DeviceBindLimit|0/1 menecer",
            (await w.Db.AuditLogs.SingleAsync(a => a.EventType == AuditEventType.StaffAlertSent)).Reason);
        Assert.Equal(StaffAlertOutcome.AlreadyAlerted, await w.Deliver(push, w.Worker, "DeviceBindLimit", Now.AddMinutes(30)));
    }

    // --- the request that raises one ----------------------------------------

    private sealed class CapturingQueue : IStaffAlertQueue
    {
        public List<StaffAlert> Items { get; } = [];
        public void Enqueue(StaffAlert alert) => Items.Add(alert);
        public ChannelReader<StaffAlert> Reader => Channel.CreateUnbounded<StaffAlert>().Reader;
    }

    private sealed class StubDeviceChanges(RequestDeviceChangeOutcome outcome) : IDeviceChangeService
    {
        public Task<RequestDeviceChangeResult> RequestAsync(Guid employeeId, string newDeviceFingerprint, string? ip, CancellationToken ct = default) =>
            Task.FromResult(new RequestDeviceChangeResult(outcome, outcome == RequestDeviceChangeOutcome.Created ? Guid.NewGuid() : null));
        public Task<IReadOnlyList<PendingDeviceChangeDto>> GetPendingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReviewDeviceChangeOutcome> ApproveAsync(Guid requestId, Guid adminId, string? ip, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReviewDeviceChangeOutcome> RejectAsync(Guid requestId, Guid adminId, string? ip, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static DeviceChangeController RequestController(RequestDeviceChangeOutcome outcome, CapturingQueue queue, Guid employeeId)
    {
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        return new DeviceChangeController(new StubDeviceChanges(outcome), queue, tenant)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", employeeId.ToString())], "test")) },
            },
        };
    }

    [Fact]
    public async Task A_new_phone_request_raises_one_alert()
    {
        var queue = new CapturingQueue();
        var employee = Guid.NewGuid();

        await RequestController(RequestDeviceChangeOutcome.Created, queue, employee).Submit(new DeviceChangeRequestBody("fp"));

        var alert = Assert.Single(queue.Items);
        Assert.Equal((TenantId, employee, StaffAlertKinds.DeviceChangeRequested), (alert.TenantId, alert.EmployeeId, alert.Kind));
    }

    [Fact]
    public async Task A_request_already_waiting_does_not_raise_another()
    {
        var queue = new CapturingQueue();

        var result = await RequestController(RequestDeviceChangeOutcome.PendingExists, queue, Guid.NewGuid()).Submit(new DeviceChangeRequestBody("fp"));

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Empty(queue.Items);
    }
}
