using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A phone the account does not know: when the poster adopts it, when it refuses, and what everyone
/// is told about the refusal.
///
/// From 21.09.2026 eleven people at Bakı Abadlıq were refused 297 times with «Bu cihaz hesabınıza bağlı
/// deyil». Every one of them had spent the allowance of three automatic adoptions in thirty days —
/// their phones' browsers kept losing the app's storage, so they kept arriving as new devices. Two
/// things kept them there. Approving their request bound only the one context it was filed from and
/// left the allowance spent, so the next lost context refused them again; and the refusal said the
/// same words whether the allowance had run out, an admin had removed the phone, or the poster was in
/// strict mode, so nobody — the employee, the admin, the Problems screen — could see which.
///
/// These tests pin both repairs, and pin the wire contract that let them ship without breaking an
/// installed app still running an older bundle: the error stays DeviceMismatch, the cause rides beside.
/// </summary>
public class DeviceRefusalTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d8");
    private const double OfficeLat = 40.4093;
    private const double OfficeLng = 49.8671;

    /// <summary>09:00 in Baku on 03.10.2026 — a working morning far from midnight, so only the device
    /// rules decide the outcome.</summary>
    private static readonly DateTime Now = new(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AttendanceController Controller { get; }
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        private readonly IQrTokenService _qr;

        public Harness(bool autoBind = true)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"device-refusal-{Guid.NewGuid()}")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Bazar qabağı",
                Latitude = OfficeLat, Longitude = OfficeLng, RadiusMeters = 150,
                ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Sahə İşçisi",
                Email = "sahe@baki.local", LocationId = LocationId,
                Role = EmployeeRole.Employee, IsActive = true,
                ActivatedAtUtc = Now.AddDays(-60), PasswordHash = "x",
            });
            Db.SaveChanges();

            _qr = new QrTokenService(Options.Create(new QrTokenOptions
            {
                Secret = "test-secret-key-for-device-refusal-tests",
                TtlSeconds = 300,
            }));

            Controller = new AttendanceController(
                Db, _qr, new AttendanceQueryService(Db), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(),
                new StubFace(), new DeviceBindingOptions { AutoBind = autoBind },
                new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AttendanceController>.Instance,
                clock: new FixedClock(Now))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", EmployeeId.ToString())], "test")),
                    },
                },
            };
        }

        public ScanRequest Scan(string fingerprint) =>
            new(_qr.Generate(LocationId, 1), fingerprint, OfficeLat, OfficeLng,
                PhotoBase64: null, ClientScanId: null, ClientTimestampUtc: null, Offline: false);

        /// <summary>A browser context the poster adopted on its own, <paramref name="daysAgo"/> days
        /// before <paramref name="asOf"/> (the scan's instant unless said otherwise).</summary>
        public void AutoBound(string fingerprint, double daysAgo, DateTime? asOf = null)
        {
            var at = (asOf ?? Now).AddDays(-daysAgo);
            Db.DeviceBindings.Add(new DeviceBinding
            {
                TenantId = TenantId, EmployeeId = EmployeeId, DeviceFingerprint = fingerprint,
                BoundVia = DeviceBindingOrigin.AutoBind, BoundAtUtc = at, LastSeenAtUtc = at, IsActive = true,
            });
            Db.AuditLogs.Add(new AuditLog
            {
                TenantId = TenantId, EmployeeId = EmployeeId,
                EventType = AuditEventType.DeviceAutoBound, CreatedAtUtc = at,
            });
            Db.SaveChanges();
        }

        /// <summary>A device-change request an admin approved, <paramref name="daysAgo"/> days before the scan.</summary>
        public void Approved(string fingerprint, double daysAgo)
        {
            var at = Now.AddDays(-daysAgo);
            Db.DeviceChangeRequests.Add(new DeviceChangeRequest
            {
                TenantId = TenantId, EmployeeId = EmployeeId, NewDeviceFingerprint = fingerprint,
                Status = DeviceChangeStatus.Approved, RequestedAtUtc = at.AddMinutes(-30), ReviewedAtUtc = at,
            });
            Db.DeviceBindings.Add(new DeviceBinding
            {
                TenantId = TenantId, EmployeeId = EmployeeId, DeviceFingerprint = fingerprint,
                BoundVia = DeviceBindingOrigin.AdminApproval, BoundAtUtc = at, LastSeenAtUtc = at, IsActive = true,
            });
            Db.AuditLogs.Add(new AuditLog
            {
                TenantId = TenantId, EmployeeId = EmployeeId,
                EventType = AuditEventType.DeviceChangeApproved, CreatedAtUtc = at,
            });
            Db.SaveChanges();
        }

        /// <summary>A phone an admin removed from this account.</summary>
        public void Revoked(string fingerprint)
        {
            Db.DeviceBindings.Add(new DeviceBinding
            {
                TenantId = TenantId, EmployeeId = EmployeeId, DeviceFingerprint = fingerprint,
                BoundVia = DeviceBindingOrigin.AutoBind, BoundAtUtc = Now.AddDays(-9), LastSeenAtUtc = Now.AddDays(-3),
                IsActive = false, RevokedAtUtc = Now.AddDays(-2),
            });
            Db.SaveChanges();
        }

        /// <summary>The reason the audit recorded for the one refusal under test — what the admin's
        /// Problems screen shows.</summary>
        public async Task<string?> RefusalReasonAsync() =>
            (await Db.AuditLogs.AsNoTracking()
                .SingleAsync(a => a.EmployeeId == EmployeeId && a.EventType == AuditEventType.CheckInRejected))
            .Reason;

        public void Dispose() => Db.Dispose();
    }

    // --- reading the endpoint's answer --------------------------------------

    private static string? Action(IActionResult r) =>
        (r as OkObjectResult)?.Value?.GetType().GetProperty("action")?.GetValue((r as OkObjectResult)!.Value) as string;

    private static string? Field(IActionResult r, string name) => r switch
    {
        ObjectResult o when o.Value is not null => o.Value.GetType().GetProperty(name)?.GetValue(o.Value) as string,
        _ => null,
    };

    private static string? Error(IActionResult r) => Field(r, "error");
    private static string? Cause(IActionResult r) => Field(r, "cause");

    // --- the window, on its own -----------------------------------------------

    [Fact]
    public void Without_an_approval_the_allowance_counts_the_last_thirty_days() =>
        Assert.Equal(Now.AddDays(-30), DeviceBindingRules.AutoBindWindowStart(Now, lastApprovalUtc: null));

    [Fact]
    public void An_approval_inside_the_thirty_days_starts_the_count_again() =>
        Assert.Equal(Now.AddDays(-4), DeviceBindingRules.AutoBindWindowStart(Now, Now.AddDays(-4)));

    [Fact]
    public void An_approval_older_than_thirty_days_changes_nothing() =>
        Assert.Equal(Now.AddDays(-30), DeviceBindingRules.AutoBindWindowStart(Now, Now.AddDays(-45)));

    // --- the allowance, at the poster ---------------------------------------

    [Fact]
    public async Task A_new_phone_inside_the_allowance_is_adopted_as_before()
    {
        using var h = new Harness();
        h.AutoBound("ctx-1", 20);
        h.AutoBound("ctx-2", 10);

        var result = await h.Controller.Scan(h.Scan("ctx-new"));

        Assert.Equal("CheckIn", Action(result));
    }

    [Fact]
    public async Task The_fourth_new_phone_in_thirty_days_is_refused_and_the_refusal_names_the_allowance()
    {
        // Mirəziz Mirsadıqov's September: three contexts adopted in four days, then a fourth refused —
        // and until now he and the admin were both told only «Bu cihaz hesabınıza bağlı deyil».
        using var h = new Harness();
        h.AutoBound("ctx-1", 18);
        h.AutoBound("ctx-2", 15);
        h.AutoBound("ctx-3", 14);

        var result = await h.Controller.Scan(h.Scan("ctx-new"));

        Assert.Equal("DeviceMismatch", Error(result));   // what an older copy of the app matches on
        Assert.Equal("DeviceBindLimit", Cause(result));  // what this copy shows the employee
        Assert.StartsWith("DeviceBindLimit", await h.RefusalReasonAsync());
    }

    [Fact]
    public async Task After_an_approval_the_poster_adopts_the_next_new_phone_itself()
    {
        // Bayramov Musa, 03.10.2026: his morning request was approved at 13:37, and at 13:41 his phone
        // was somebody new again. The approval now reopens the allowance, so that phone gets in.
        using var h = new Harness();
        h.AutoBound("ctx-1", 24);
        h.AutoBound("ctx-2", 7);
        h.AutoBound("ctx-3", 1);
        h.Approved("ctx-approved", daysAgo: 0.003);   // four minutes before the scan

        var result = await h.Controller.Scan(h.Scan("ctx-after-approval"));

        Assert.Equal("CheckIn", Action(result));
        Assert.True(await h.Db.DeviceBindings.AnyAsync(d =>
            d.DeviceFingerprint == "ctx-after-approval" && d.IsActive && d.BoundVia == DeviceBindingOrigin.AutoBind));
    }

    [Fact]
    public async Task An_approval_restarts_the_allowance_it_does_not_lift_it()
    {
        // The guard against private browsing is still there: three adoptions after the approval spend
        // the allowance again, and the fourth goes back to an admin.
        using var h = new Harness();
        h.AutoBound("ctx-1", 20);
        h.AutoBound("ctx-2", 18);
        h.AutoBound("ctx-3", 16);
        h.Approved("ctx-approved", daysAgo: 10);
        h.AutoBound("ctx-4", 8);
        h.AutoBound("ctx-5", 5);
        h.AutoBound("ctx-6", 2);

        var result = await h.Controller.Scan(h.Scan("ctx-new"));

        Assert.Equal("DeviceBindLimit", Cause(result));
    }

    [Fact]
    public async Task An_approval_from_before_the_thirty_days_does_not_reopen_anything()
    {
        using var h = new Harness();
        h.Approved("ctx-approved", daysAgo: 45);
        h.AutoBound("ctx-1", 20);
        h.AutoBound("ctx-2", 10);
        h.AutoBound("ctx-3", 5);

        var result = await h.Controller.Scan(h.Scan("ctx-new"));

        Assert.Equal("DeviceBindLimit", Cause(result));
    }

    // --- the other two refusals ---------------------------------------------

    [Fact]
    public async Task A_phone_an_admin_removed_is_refused_as_removed()
    {
        using var h = new Harness();
        h.AutoBound("ctx-1", 3);
        h.Revoked("ctx-removed");

        var result = await h.Controller.Scan(h.Scan("ctx-removed"));

        Assert.Equal("DeviceMismatch", Error(result));
        Assert.Equal("DeviceRevoked", Cause(result));
        Assert.StartsWith("DeviceRevoked", await h.RefusalReasonAsync());
    }

    [Fact]
    public async Task Strict_mode_answers_exactly_as_it_did()
    {
        // Nothing here is new information, so nothing is added: no cause, the old audit code.
        using var h = new Harness(autoBind: false);
        h.AutoBound("ctx-1", 3);

        var result = await h.Controller.Scan(h.Scan("ctx-other"));

        Assert.Equal("DeviceMismatch", Error(result));
        Assert.Null(Cause(result));
        Assert.StartsWith("DeviceMismatch", await h.RefusalReasonAsync());
    }

    // --- what the admin sees before deciding ----------------------------------

    [Fact]
    public async Task A_pending_request_shows_the_admin_the_allowance_the_poster_refused_on()
    {
        // GetPendingAsync reads the wall clock, so these dates are relative to it rather than to Now.
        using var h = new Harness();
        var wall = DateTime.UtcNow;
        h.AutoBound("ctx-1", 18, asOf: wall);
        h.AutoBound("ctx-2", 15, asOf: wall);
        h.AutoBound("ctx-3", 14, asOf: wall);
        h.Db.DeviceChangeRequests.Add(new DeviceChangeRequest
        {
            TenantId = TenantId, EmployeeId = h.EmployeeId, NewDeviceFingerprint = "ctx-new",
            Status = DeviceChangeStatus.Pending, RequestedAtUtc = wall,
        });
        h.Db.SaveChanges();

        var pending = Assert.Single(await new DeviceChangeService(h.Db, new DeviceBindingOptions()).GetPendingAsync());

        Assert.True(pending.AutoBindLimitReached);
        Assert.Equal(3, pending.RecentNewDevices);
    }

    [Fact]
    public async Task A_first_new_phone_is_not_flagged_as_a_forgetful_one()
    {
        using var h = new Harness();
        h.AutoBound("ctx-1", 25, asOf: DateTime.UtcNow);
        h.Db.DeviceChangeRequests.Add(new DeviceChangeRequest
        {
            TenantId = TenantId, EmployeeId = h.EmployeeId, NewDeviceFingerprint = "ctx-new",
            Status = DeviceChangeStatus.Pending, RequestedAtUtc = DateTime.UtcNow,
        });
        h.Db.SaveChanges();

        var pending = Assert.Single(await new DeviceChangeService(h.Db, new DeviceBindingOptions()).GetPendingAsync());

        Assert.False(pending.AutoBindLimitReached);
        Assert.Equal(1, pending.RecentNewDevices);
    }

    // --- stubs --------------------------------------------------------------

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("f");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private sealed class StubFace : IFaceMatchService
    {
        public bool Enabled => false;
        public Task<FaceMatchOutcome> CompareAsync(byte[] r, byte[] c, CancellationToken ct = default)
            => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));
        public Task<int> DetectFaceCountAsync(byte[] p, CancellationToken ct = default) => Task.FromResult(-1);
    }
}
