using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// An admin's edit, a hand-written day and an approved self-correction judge lateness exactly as the scan does:
/// that day's start + the SHIFT's own threshold, on the calendar of the day the record belongs to. The edit path
/// compared times of day, so for a 23:50 shift with fifteen minutes' grace (due 00:05 the next morning) a
/// corrected 23:55 arrival read as late; the create path also took the branch's threshold instead of the shift's.
/// </summary>
public class AdminNightLatenessTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000ad4a");
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");
    private static readonly DateOnly D0 = new(2026, 10, 5);
    private static readonly DateOnly D1 = new(2026, 10, 6);

    private static DateTime Utc(DateOnly date, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(hour, minute)), Baku);

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminAttendanceController Admin { get; }
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid NightWorker { get; } = Guid.NewGuid();
        public Guid NamedShiftWorker { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"admin-night-{Guid.NewGuid()}").Options, tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = Branch, TenantId = TenantId, Name = "Anbar", Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(23, 50), ShiftEnd = new TimeOnly(8, 0), LateThresholdMinutes = 15,
                WorkDaysMask = 127, QrVersion = 1, IsActive = true,
            });
            var generous = Guid.NewGuid();
            Db.Schedules.Add(new Schedule
            {
                Id = generous, TenantId = TenantId, Name = "Səhər 30", ShiftStart = new TimeOnly(9, 0),
                ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 30, WorkDaysMask = 127,
            });
            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, null));
            Db.Employees.Add(Person(NightWorker, "Gece", EmployeeRole.Employee, null));
            Db.Employees.Add(Person(NamedShiftWorker, "Seher", EmployeeRole.Employee, generous));
            Db.SaveChanges();

            Admin = new AdminAttendanceController(
                Db, new StubSummary(), new StubFaceQueue(), new StubPhoto(),
                NullLogger<AdminAttendanceController>.Instance, new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", AdminId.ToString()), new Claim("role", "Admin")], "test")),
                    },
                },
            };
        }

        private Employee Person(Guid id, string name, EmployeeRole role, Guid? scheduleId) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, IsActive = true, PasswordHash = "x",
            LocationId = Branch, ScheduleId = scheduleId, ActivatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        public Guid NightRecord(DateTime inUtc)
        {
            var id = Guid.NewGuid();
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = id, TenantId = TenantId, EmployeeId = NightWorker, LocationId = Branch, AttendanceDate = D0,
                CheckInAtUtc = inUtc, CheckOutAtUtc = Utc(D1, 8, 0), Status = AttendanceStatus.Late,
            });
            Db.SaveChanges();
            return id;
        }

        public AttendanceStatus StatusOf(Guid id) => Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == id).Status;

        public void Dispose() => Db.Dispose();
    }

    [Theory]
    [InlineData(0, 23, 55, AttendanceStatus.OnTime)]  // was Late: 23:50 + 15 wrapped to 00:05 the same evening
    [InlineData(1, 0, 3, AttendanceStatus.OnTime)]
    [InlineData(1, 0, 5, AttendanceStatus.OnTime)]
    [InlineData(1, 0, 6, AttendanceStatus.Late)]
    public async Task An_edited_night_arrival_is_judged_on_the_calendar(int day, int hour, int minute, AttendanceStatus expected)
    {
        using var h = new Harness();
        var id = h.NightRecord(Utc(D0, 23, 58));

        var result = await h.Admin.Update(id, new AdminAttendanceUpdateRequest(
            Utc(D0.AddDays(day), hour, minute), null, "Giriş vaxtı düzəldildi"));

        Assert.IsNotType<BadRequestObjectResult>(result);
        Assert.Equal(expected, h.StatusOf(id));
    }

    [Theory]
    [InlineData(9, 20, AttendanceStatus.OnTime)]   // inside the SHIFT's 30 minutes — the branch's 15 made it late
    [InlineData(9, 31, AttendanceStatus.Late)]
    public async Task A_hand_written_day_uses_the_shifts_own_grace(int hour, int minute, AttendanceStatus expected)
    {
        using var h = new Harness();

        await h.Admin.Create(new AdminAttendanceCreateRequest(
            h.NamedShiftWorker, D0, Utc(D0, hour, minute), Utc(D0, 18, 0), "Telefonu xarab idi"));

        var record = Assert.Single(h.Db.AttendanceRecords.AsNoTracking().Where(r => r.EmployeeId == h.NamedShiftWorker));
        Assert.Equal(expected, record.Status);
    }

    [Theory]
    [InlineData(0, 23, 55, AttendanceStatus.OnTime)]
    [InlineData(1, 0, 6, AttendanceStatus.Late)]
    public async Task A_hand_written_night_is_judged_on_the_calendar(int day, int hour, int minute, AttendanceStatus expected)
    {
        using var h = new Harness();

        await h.Admin.Create(new AdminAttendanceCreateRequest(
            h.NightWorker, D0, Utc(D0.AddDays(day), hour, minute), Utc(D1, 8, 0), "Telefonu xarab idi"));

        var record = Assert.Single(h.Db.AttendanceRecords.AsNoTracking().Where(r => r.EmployeeId == h.NightWorker));
        Assert.Equal(D0, record.AttendanceDate);
        Assert.Equal(expected, record.Status);
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubFaceQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public System.Threading.Channels.ChannelReader<FaceMatchJob> Reader =>
            System.Threading.Channels.Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

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
}
