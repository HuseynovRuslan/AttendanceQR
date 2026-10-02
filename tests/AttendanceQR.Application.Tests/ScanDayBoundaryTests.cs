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
/// Where one working day ends and the next begins — in Baku, not in Greenwich.
///
/// On the night of 01→02.10.2026 six people at Heydər Əliyev Mərkəzi worked a presidential event.
/// They arrived at 07:35–07:50, checked out at 23:53–23:56, and a few minutes later tried to start
/// the new day. The poster refused every one of them: «Giriş və çıxış artıq qeydə alınıb». They tried
/// again at one in the morning, and again after that — forty-one refusals between 00:01:10 and
/// 01:26:23, the manager alone seventeen times. The first scan that worked anywhere on that site was
/// at 04:48. Six hours of six people's work exists only in the audit log.
///
/// The cause was one line: the scan filed a day by the UTC calendar while every other part of the
/// system — the nightly summary, the tabel, the reports, the reminders, the field visits — filed it
/// by Baku's. Baku is UTC+4, so for the four hours after midnight the scan was still writing on
/// yesterday's page, and yesterday's page had been signed and closed at 23:53.
///
/// These tests pin the boundary at Baku midnight. Two of them describe the night above; the rest are
/// there because moving the boundary moves which branch a 00:00–04:00 scan takes — a night worker's
/// exit at one in the morning used to find its own day and now reaches the same record through the
/// overnight-close path instead. That is the regression this change could cause, so it is tested
/// from both sides of midnight.
/// </summary>
public class ScanDayBoundaryTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d7");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private const double OfficeLat = 40.4093;
    private const double OfficeLng = 49.8671;

    /// <summary>A Baku wall-clock instant, as the UTC the endpoint and the database see.</summary>
    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), Baku);

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

        /// <param name="nowUtc">The instant the scan happens — always pinned, so the result never
        /// depends on the hour the suite runs at.</param>
        /// <param name="shiftStart">The branch's hours. End earlier than start makes it overnight.</param>
        public Harness(DateTime nowUtc, TimeOnly shiftStart, TimeOnly shiftEnd)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"dayboundary-{Guid.NewGuid()}")
                    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Heydər Əliyev Mərkəzi",
                Latitude = OfficeLat, Longitude = OfficeLng, RadiusMeters = 150,
                ShiftStart = shiftStart, ShiftEnd = shiftEnd,
                LateThresholdMinutes = 15, WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, FullName = "Tədbir İşçisi",
                Email = "tedbir@baki.local", LocationId = LocationId,
                Role = EmployeeRole.Employee, IsActive = true,
                ActivatedAtUtc = Utc(2026, 1, 1, 8, 0), PasswordHash = "x",
            });
            Db.SaveChanges();

            _qr = new QrTokenService(Options.Create(new QrTokenOptions
            {
                Secret = "test-secret-key-for-day-boundary-tests",
                TtlSeconds = 300,
            }));

            Controller = new AttendanceController(
                Db, _qr, new AttendanceQueryService(Db), new StubPhoto(), new StubQueue(), new PhotoUploadQueue(),
                new StubFace(), new DeviceBindingOptions { AutoBind = true },
                new AppOptions { TimeZone = "Asia/Baku" },
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AttendanceController>.Instance,
                clock: new FixedClock(nowUtc))
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

        public ScanRequest Scan() =>
            new(_qr.Generate(LocationId, 1), "device-fp-1", OfficeLat, OfficeLng,
                PhotoBase64: null, ClientScanId: null, ClientTimestampUtc: null, Offline: false);

        /// <summary>A day already on the books, in Baku wall-clock times.</summary>
        public void SeedDay(DateOnly date, DateTime inUtc, DateTime? outUtc)
        {
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LocationId = LocationId,
                AttendanceDate = date, CheckInAtUtc = inUtc, CheckOutAtUtc = outUtc,
                Status = AttendanceStatus.OnTime,
            });
            Db.SaveChanges();
        }

        public async Task<List<AttendanceRecord>> RecordsAsync() =>
            await Db.AttendanceRecords.AsNoTracking().OrderBy(r => r.CheckInAtUtc).ToListAsync();

        public void Dispose() => Db.Dispose();
    }

    // --- reading the endpoint's answer --------------------------------------

    private static string? Action(IActionResult r) =>
        (r as OkObjectResult)?.Value?.GetType().GetProperty("action")?.GetValue((r as OkObjectResult)!.Value) as string;

    private static string? Error(IActionResult r) => r switch
    {
        ObjectResult o when o.Value is not null =>
            o.Value.GetType().GetProperty("error")?.GetValue(o.Value) as string,
        _ => null,
    };

    // --- the night of 01→02.10.2026 -----------------------------------------

    [Fact]
    public async Task A_finished_day_does_not_block_the_next_one_at_one_minute_past_midnight()
    {
        // Exactly the refusal six people met forty-one times: out at 23:53, back at 00:01, and the
        // scan landed on a day that was already closed because UTC had not reached midnight yet.
        using var h = new Harness(Utc(2026, 10, 2, 0, 1), new TimeOnly(8, 0), new TimeOnly(18, 0));
        h.SeedDay(new DateOnly(2026, 10, 1), Utc(2026, 10, 1, 7, 38), Utc(2026, 10, 1, 23, 53));

        var result = await h.Controller.Scan(h.Scan());

        Assert.Null(Error(result));
        Assert.Equal("CheckIn", Action(result));

        var records = await h.RecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal(new DateOnly(2026, 10, 2), records[^1].AttendanceDate);
        Assert.Null(records[^1].CheckOutAtUtc);
        // Yesterday is untouched: the sixteen hours they did work stay exactly as they were.
        Assert.Equal(Utc(2026, 10, 1, 23, 53), records[0].CheckOutAtUtc);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 30)]
    [InlineData(3, 59)]
    public async Task A_scan_after_midnight_is_filed_under_the_baku_date(int hour, int minute)
    {
        // The whole 00:00–04:00 band, not just the minute it was reported in. Before the fix every
        // one of these produced a record dated the first of October.
        using var h = new Harness(Utc(2026, 10, 2, hour, minute), new TimeOnly(8, 0), new TimeOnly(18, 0));

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("CheckIn", Action(result));
        Assert.Equal(new DateOnly(2026, 10, 2), Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task The_new_day_can_be_closed_normally()
    {
        // The other half of the night: having started at 00:05 they must be able to finish. A day that
        // can be opened and not closed is the «Çıxış yoxdur» row that pays zero hours.
        using var h = new Harness(Utc(2026, 10, 2, 6, 30), new TimeOnly(8, 0), new TimeOnly(18, 0));
        h.SeedDay(new DateOnly(2026, 10, 1), Utc(2026, 10, 1, 7, 38), Utc(2026, 10, 1, 23, 53));
        h.SeedDay(new DateOnly(2026, 10, 2), Utc(2026, 10, 2, 0, 5), null);

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("CheckOut", Action(result));
        var records = await h.RecordsAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal(Utc(2026, 10, 2, 6, 30), records[^1].CheckOutAtUtc);
    }

    // --- the regression this change could cause -----------------------------

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 0)]
    [InlineData(3, 45)]
    public async Task A_night_worker_leaving_after_midnight_still_closes_the_shift_they_started(int hour, int minute)
    {
        // The branch that changes. Before the fix this scan found yesterday's record as «today's» and
        // closed it directly; now it finds no record for the new day and reaches the same row through
        // the overnight-close path. The ANSWER must not move: one record, closed, no stray second day.
        using var h = new Harness(Utc(2026, 10, 2, hour, minute), new TimeOnly(19, 0), new TimeOnly(7, 0));
        h.SeedDay(new DateOnly(2026, 10, 1), Utc(2026, 10, 1, 19, 2), null);

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("CheckOut", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(new DateOnly(2026, 10, 1), record.AttendanceDate);
        Assert.Equal(Utc(2026, 10, 2, hour, minute), record.CheckOutAtUtc);
    }

    [Fact]
    public async Task A_night_worker_leaving_in_the_morning_is_unchanged()
    {
        // The ordinary overnight exit, well clear of the boundary — the control for the test above.
        using var h = new Harness(Utc(2026, 10, 2, 7, 10), new TimeOnly(19, 0), new TimeOnly(7, 0));
        h.SeedDay(new DateOnly(2026, 10, 1), Utc(2026, 10, 1, 19, 2), null);

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("CheckOut", Action(result));
        var record = Assert.Single(await h.RecordsAsync());
        Assert.Equal(new DateOnly(2026, 10, 1), record.AttendanceDate);
    }

    [Fact]
    public async Task A_night_worker_arriving_in_the_evening_opens_the_evening_s_own_date()
    {
        // Before and after the fix this is the same date — 21:00 in Baku is still the first of October
        // in Greenwich. It is here so a future change to the boundary cannot move it unnoticed.
        using var h = new Harness(Utc(2026, 10, 1, 21, 0), new TimeOnly(19, 0), new TimeOnly(7, 0));

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("CheckIn", Action(result));
        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    // --- the ordinary day, which must not notice any of this ----------------

    [Theory]
    [InlineData(8, 5)]
    [InlineData(13, 0)]
    [InlineData(23, 30)]
    public async Task A_scan_before_midnight_is_filed_under_that_same_day(int hour, int minute)
    {
        using var h = new Harness(Utc(2026, 10, 1, hour, minute), new TimeOnly(8, 0), new TimeOnly(18, 0));

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("CheckIn", Action(result));
        Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(await h.RecordsAsync()).AttendanceDate);
    }

    [Fact]
    public async Task A_second_scan_minutes_after_a_check_out_is_still_refused()
    {
        // The guard that stops a double-tap opening a fresh day must survive the move. Five minutes is
        // the cool-off; this is two, and on the far side of midnight where the new code path runs.
        using var h = new Harness(Utc(2026, 10, 2, 0, 2), new TimeOnly(8, 0), new TimeOnly(18, 0));
        h.SeedDay(new DateOnly(2026, 10, 1), Utc(2026, 10, 1, 7, 38), Utc(2026, 10, 2, 0, 0));

        var result = await h.Controller.Scan(h.Scan());

        Assert.Equal("AlreadyCompleted", Error(result));
        Assert.Single(await h.RecordsAsync());
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
