using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
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
/// A worker scans the poster in the morning, spends the day at a site, and goes home from there. The
/// poster is never passed again, so their attendance record kept a check-in and no check-out — and an
/// open record scores ZERO hours, not partial ones. The only fix was an admin noticing it on
/// /admin/open-records and typing the time in, which made someone's pay depend on someone else's
/// attention. (It is how Kamran Cavadzadə's 10.08 day was found: 07:23 in, nothing out.)
///
/// The field check-out now closes that day at the moment they left the site. These pin the guard rails,
/// because this writes to the record pay is calculated from.
/// </summary>
public class FieldCheckOutClosesTheDayTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d4");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public FieldVisitController AsWorker { get; }
        public Guid WorkerId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();
        public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"fv-close-{Guid.NewGuid()}").Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Baş ofis", Latitude = 40.4, Longitude = 49.8,
                RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });
            Db.Employees.Add(new Employee
            {
                Id = WorkerId, TenantId = TenantId, FullName = "Sahə İşçisi", Email = "s@t.local",
                LocationId = LocationId, Role = EmployeeRole.Employee, IsActive = true,
                ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x", CanFieldCheckIn = true,
            });
            Db.SaveChanges();

            AsWorker = new FieldVisitController(Db, new StubPhoto(), new StubPush(),
                new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", WorkerId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Employee)),
                        }, "test")),
                    },
                },
            };
        }

        /// <summary>A field visit already in progress, ready to be checked out of.</summary>
        public FieldVisit OpenVisit(DateOnly? date = null)
        {
            var visit = new FieldVisit
            {
                TenantId = TenantId, EmployeeId = WorkerId, VisitDate = date ?? Today,
                Status = FieldVisitStatus.CheckedIn, CheckInAtUtc = DateTime.UtcNow.AddHours(-6),
                TargetLabel = "Şəhər parkı",
            };
            Db.FieldVisits.Add(visit);
            Db.SaveChanges();
            return visit;
        }

        /// <summary>The morning poster scan, with no check-out — the state this whole thing is about.</summary>
        public AttendanceRecord OpenDay(DateTime? checkInAtUtc = null, DateOnly? date = null)
        {
            var record = new AttendanceRecord
            {
                TenantId = TenantId, EmployeeId = WorkerId, LocationId = LocationId,
                AttendanceDate = date ?? Today,
                CheckInAtUtc = checkInAtUtc ?? DateTime.UtcNow.AddHours(-9),
            };
            Db.AttendanceRecords.Add(record);
            Db.SaveChanges();
            return record;
        }

        public void Dispose() => Db.Dispose();
    }

    private static FieldCheckOutRequest Leaving() => new(40.4, 49.8, null, null);

    [Fact]
    public async Task Leaving_the_site_closes_the_open_poster_day_at_the_same_moment()
    {
        using var h = new Harness();
        var visit = h.OpenVisit();
        var record = h.OpenDay();

        await h.AsWorker.CheckOut(visit.Id, Leaving());

        var closed = await h.Db.AttendanceRecords.FirstAsync(r => r.Id == record.Id);
        var left = (await h.Db.FieldVisits.FirstAsync(v => v.Id == visit.Id)).CheckOutAtUtc;
        Assert.Equal(left, closed.CheckOutAtUtc);
        // Attributed to the visit, so the admin can tell this time from a poster scan AND from a
        // typed-in one — it is neither.
        Assert.Equal(visit.Id, closed.ClosedByFieldVisitId);
        Assert.Null(closed.ManualByEmployeeId);
    }

    [Fact]
    public async Task A_day_that_was_already_closed_is_left_alone()
    {
        // They did pass the poster on the way home. That scan is the real departure; overwriting it
        // with the field time would move a completed day for no reason.
        using var h = new Harness();
        var visit = h.OpenVisit();
        var record = h.OpenDay();
        var realCheckOut = DateTime.UtcNow.AddHours(-1);
        record.CheckOutAtUtc = realCheckOut;
        await h.Db.SaveChangesAsync();

        await h.AsWorker.CheckOut(visit.Id, Leaving());

        var after = await h.Db.AttendanceRecords.FirstAsync(r => r.Id == record.Id);
        Assert.Equal(realCheckOut, after.CheckOutAtUtc);
        Assert.Null(after.ClosedByFieldVisitId);
    }

    [Fact]
    public async Task A_check_in_later_than_the_departure_is_never_closed_into_negative_hours()
    {
        // The pathological case, and a real one: the worker forgot to scan in the morning and did it
        // at the very end of the day (exactly what happened on 10.08 — check-in stamped 18 seconds
        // before check-out). Closing that day at the field departure would end it BEFORE it began.
        using var h = new Harness();
        var visit = h.OpenVisit();
        var record = h.OpenDay(checkInAtUtc: DateTime.UtcNow.AddHours(1));

        await h.AsWorker.CheckOut(visit.Id, Leaving());

        var after = await h.Db.AttendanceRecords.FirstAsync(r => r.Id == record.Id);
        Assert.Null(after.CheckOutAtUtc);
        Assert.Null(after.ClosedByFieldVisitId);
    }

    [Fact]
    public async Task Another_days_open_record_is_not_touched()
    {
        // A visit only ever closes its OWN date. Yesterday's forgotten check-out is a different
        // problem with a different real departure time, and guessing it here would be inventing data.
        using var h = new Harness();
        var visit = h.OpenVisit();
        var yesterday = h.OpenDay(checkInAtUtc: DateTime.UtcNow.AddDays(-1), date: h.Today.AddDays(-1));

        await h.AsWorker.CheckOut(visit.Id, Leaving());

        var after = await h.Db.AttendanceRecords.FirstAsync(r => r.Id == yesterday.Id);
        Assert.Null(after.CheckOutAtUtc);
    }

    [Fact]
    public async Task A_field_day_with_no_poster_scan_at_all_still_works()
    {
        // The QR-less field day: no record to close, and none is invented here — the reporting layer
        // already synthesises the day from the visit. This must not throw or create a phantom record.
        using var h = new Harness();
        var visit = h.OpenVisit();

        var result = await h.AsWorker.CheckOut(visit.Id, Leaving());

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await h.Db.AttendanceRecords.AnyAsync());
        Assert.Equal(FieldVisitStatus.Completed, (await h.Db.FieldVisits.FirstAsync()).Status);
    }

    [Fact]
    public async Task The_departure_still_stands_even_if_closing_the_day_could_not_happen()
    {
        // The house rule, restated for this path: the field check-out is committed first and on its
        // own. Whatever happens to the attendance record afterwards, the worker has left.
        using var h = new Harness();
        var visit = h.OpenVisit();
        h.OpenDay(checkInAtUtc: DateTime.UtcNow.AddHours(1)); // refused by the ordering guard above

        var result = await h.AsWorker.CheckOut(visit.Id, Leaving());

        Assert.IsType<OkObjectResult>(result);
        var after = await h.Db.FieldVisits.FirstAsync(v => v.Id == visit.Id);
        Assert.Equal(FieldVisitStatus.Completed, after.Status);
        Assert.NotNull(after.CheckOutAtUtc);
    }

    [Fact]
    public async Task Replaying_the_check_out_does_not_move_the_day_a_second_time()
    {
        // The check-out is idempotent (a lost 200, an LTE handover, a retry). The day it closed must
        // not drift to the time of the retry.
        using var h = new Harness();
        var visit = h.OpenVisit();
        var record = h.OpenDay();

        await h.AsWorker.CheckOut(visit.Id, Leaving());
        var firstClose = (await h.Db.AttendanceRecords.FirstAsync(r => r.Id == record.Id)).CheckOutAtUtc;
        await h.AsWorker.CheckOut(visit.Id, Leaving());

        var after = await h.Db.AttendanceRecords.FirstAsync(r => r.Id == record.Id);
        Assert.Equal(firstClose, after.CheckOutAtUtc);
    }

    private sealed class StubPhoto : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("checkins/k.jpg");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("reference/k.jpg");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("fieldwork/k.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult($"https://r2/{key}");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    private sealed class StubPush : IPushNotifier
    {
        public Task<int> NotifyEmployeesAsync(IReadOnlyCollection<Guid> ids, string t, string b, string? u, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
