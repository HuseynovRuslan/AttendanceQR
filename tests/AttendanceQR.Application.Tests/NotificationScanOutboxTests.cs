using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Api.Notifications;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Domain.Notifications;
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
/// The producer end to end: a real scan through <see cref="AttendanceController"/> writes its outbox event in the
/// check-in's own save — and writes nothing at all in a company where nobody wants attendance notifications. The scan's
/// answer is the same either way: the notification module never changes what a check-in does.
/// </summary>
public class NotificationScanOutboxTests
{
    private const double Lat = 40.4093, Lng = 49.8671;

    private sealed class Rig : IDisposable
    {
        public AccessWorld World { get; } = new();
        public AppDbContext Db { get; }
        public FakeHint Hint { get; } = new();
        public AttendanceController Controller { get; }
        private readonly QrTokenService _qr = new(Options.Create(new QrTokenOptions { Secret = "notification-outbox-scan-tests", TtlSeconds = 300 }));

        public Rig()
        {
            using (var setup = World.Context(AccessWorld.TenantA))
            {
                var b1 = setup.Locations.Single(l => l.Id == World.B1);
                (b1.Latitude, b1.Longitude, b1.RadiusMeters) = (Lat, Lng, 150);
                (b1.ShiftStart, b1.ShiftEnd, b1.LateThresholdMinutes) = (new TimeOnly(9, 0), new TimeOnly(18, 0), 15);
                setup.SaveChanges();
            }
            Db = World.Context(AccessWorld.TenantA);
            var cache = new MemoryCache(new MemoryCacheOptions());
            Controller = new AttendanceController(
                Db, _qr, new StubQuery(), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(), new StubFace(),
                new DeviceBindingOptions { AutoBind = true }, new AppOptions { TimeZone = "Asia/Baku" },
                cache, NullLogger<AttendanceController>.Instance,
                outbox: new NotificationOutbox(cache, NullLogger<NotificationOutbox>.Instance, Hint))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", World.E1.ToString()), new Claim("role", nameof(EmployeeRole.Employee))], "test")),
                    },
                },
            };
        }

        public ScanRequest Scan() => new(_qr.Generate(World.B1, 1), "device-fp-notif", Lat, Lng, PhotoBase64: null);

        public void Dispose() => World.Dispose();
    }

    [Fact]
    public async Task Where_nobody_wants_notifications_a_check_in_writes_no_event()
    {
        using var r = new Rig();

        var result = await r.Controller.Scan(r.Scan());

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(r.World.Context(AccessWorld.TenantA).AttendanceRecords);
        Assert.Empty(r.World.Context(AccessWorld.TenantA).NotificationEvents);
        Assert.Empty(r.Hint.Tenants);
    }

    [Fact]
    public async Task Where_somebody_does_the_check_in_and_its_event_commit_together_and_the_worker_is_woken()
    {
        using var r = new Rig();
        using (var db = r.World.Context(AccessWorld.TenantA))
        {
            db.NotificationPreferences.Add(new NotificationPreference { EmployeeId = r.World.Admin1, EnabledTypes = NotificationTypes.CheckIn });
            db.SaveChanges();
        }

        Assert.IsType<OkObjectResult>(await r.Controller.Scan(r.Scan()));

        var db2 = r.World.Context(AccessWorld.TenantA);
        var record = Assert.Single(db2.AttendanceRecords.AsNoTracking());
        var e = Assert.Single(db2.NotificationEvents.AsNoTracking());
        Assert.Equal(NotificationEventKinds.CheckIn, e.Kind);
        Assert.Equal(record.Id, e.AttendanceRecordId);
        Assert.Equal(record.CheckInAtUtc, e.OccurredAtUtc);
        Assert.Equal(r.World.E1, e.SubjectEmployeeId);
        Assert.Equal(NotificationEventStatus.Pending, e.Status);
        Assert.Equal([AccessWorld.TenantA], r.Hint.Tenants);
    }

    // --- stubs: the scan path touches none of them without a photo ----------------------------------------------

    internal sealed class FakeHint : INotificationHint
    {
        public List<Guid> Tenants { get; } = [];
        public void Ready(Guid tenantId) => Tenants.Add(tenantId);
        public ChannelReader<Guid> Reader => Channel.CreateUnbounded<Guid>().Reader;
    }

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
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

    private sealed class StubQuery : IAttendanceQueryService
    {
        public Task<IReadOnlyList<AttendanceRecordDto>> GetOwnRecordsAsync(Guid e, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AttendanceRecordDto>>(new List<AttendanceRecordDto>());
        public Task<AttendanceRecordDto?> GetTodayAsync(Guid e, DateOnly date, CancellationToken ct = default)
            => Task.FromResult<AttendanceRecordDto?>(null);
        public Task<(AttendanceAccess Access, IReadOnlyList<AttendanceRecordDto> Records)> GetForEmployeeAsync(
            Guid t, Guid r, EmployeeRole role, CancellationToken ct = default)
            => Task.FromResult((AttendanceAccess.Forbidden, (IReadOnlyList<AttendanceRecordDto>)new List<AttendanceRecordDto>()));
    }
}
