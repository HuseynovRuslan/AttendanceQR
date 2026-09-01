using System.Security.Claims;
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
/// Deleting an attendance record is the one action here with no undo, so the boundary is pinned the
/// same way the field-visit and device-binding surfaces pin theirs: a Manager reaches only
/// Role==Employee workers in the branches they manage — the 2026-08-08 P0 rule — and never another
/// tenant, whatever id they pass.
///
/// The endpoint exists for a PHANTOM day: a night worker whose shift was not known to be a night
/// shift scans twice in the morning, the first scan opens a day that never happened and the second
/// closes it. That row then blocks every later scan with `AlreadyCompleted`, and editing cannot clear
/// it — any times on that date keep the block. Without this endpoint the only fix was a hand-written
/// DELETE against production, which is worse in every way, including that nothing recorded it.
/// </summary>
public class RecordDeleteScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminAttendanceController AsManager { get; }
        public AdminAttendanceController AsAdmin { get; }
        public Guid BranchA { get; } = Guid.NewGuid();   // managed
        public Guid BranchB { get; } = Guid.NewGuid();   // same tenant, NOT managed
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid SameBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();
        public Guid SameBranchManagerId { get; } = Guid.NewGuid();
        public Guid OtherBranchEmployeeId { get; } = Guid.NewGuid();
        public Guid OtherTenantEmployeeId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantA);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"rec-del-{Guid.NewGuid()}")
                .Options;
            Db = new AppDbContext(options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            Db.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = ManagerId, LocationId = BranchA, TenantId = TenantA });

            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchEmployeeId, "Filial İşçisi", EmployeeRole.Employee, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchAdminId, "Filial Admini", EmployeeRole.Admin, BranchA, TenantA));
            Db.Employees.Add(Person(SameBranchManagerId, "İkinci Menecer", EmployeeRole.Manager, BranchA, TenantA));
            Db.Employees.Add(Person(OtherBranchEmployeeId, "Başqa Filial", EmployeeRole.Employee, BranchB, TenantA));
            Db.Employees.Add(Person(OtherTenantEmployeeId, "Başqa Tenant", EmployeeRole.Employee, BranchA, TenantB));
            Db.SaveChanges();

            AsManager = Controller(ManagerId, EmployeeRole.Manager);
            AsAdmin = Controller(SameBranchAdminId, EmployeeRole.Admin);
        }

        private AdminAttendanceController Controller(Guid callerId, EmployeeRole role)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim("sub", callerId.ToString()),
                new Claim("role", role.ToString()),
            }, "test");
            return new AdminAttendanceController(
                Db, new StubSummary(), new StubFaceQueue(), Photo,
                NullLogger<AdminAttendanceController>.Instance,
                new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
                },
            };
        }

        public StubPhoto Photo { get; } = new();

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId, Guid tenantId) => new()
        {
            Id = id, TenantId = tenantId, FullName = name, Role = role, LocationId = locationId,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        public AttendanceRecord Record(Guid employeeId, Guid branchId, Guid tenantId, string? photoKey = null)
        {
            var r = new AttendanceRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, LocationId = branchId,
                AttendanceDate = new DateOnly(2026, 9, 1),
                CheckInAtUtc = new DateTime(2026, 9, 1, 2, 46, 0, DateTimeKind.Utc),
                CheckOutAtUtc = new DateTime(2026, 9, 1, 2, 53, 0, DateTimeKind.Utc),
                CheckInPhotoKey = photoKey,
            };
            Db.AttendanceRecords.Add(r);
            Db.SaveChanges();
            return r;
        }

        public bool Exists(Guid id) => Db.AttendanceRecords.IgnoreQueryFilters().Any(r => r.Id == id);

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public List<DateOnly> Regenerated { get; } = new();
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
        {
            Regenerated.Add(date);
            return Task.FromResult(0);
        }
    }

    private sealed class StubFaceQueue : IFaceMatchQueue
    {
        private readonly System.Threading.Channels.Channel<FaceMatchJob> _c =
            System.Threading.Channels.Channel.CreateUnbounded<FaceMatchJob>();
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public System.Threading.Channels.ChannelReader<FaceMatchJob> Reader => _c.Reader;
    }

    private sealed class StubPhoto : IPhotoStorageService
    {
        public List<string> Deleted { get; } = new();
        public Task<string> UploadCheckInPhotoAsync(Guid employeeId, Guid recordId, byte[] webpBytes, CancellationToken ct = default) => Task.FromResult("key");
        public Task<string> UploadReferencePhotoAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default) => Task.FromResult("key");
        public Task<string> UploadAvatarAsync(Guid employeeId, byte[] webpBytes, CancellationToken ct = default) => Task.FromResult("key");
        public Task<string> UploadFieldWorkPhotoAsync(Guid tenantId, Guid visitId, byte[] jpegBytes, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult("url");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string prefix, DateTime olderThanUtc, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
        {
            Deleted.AddRange(keys);
            return Task.FromResult(keys.Count);
        }
    }

    private static void AssertForbidden(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
    }

    [Fact]
    public async Task A_manager_may_clear_a_phantom_day_for_their_own_branch_worker()
    {
        using var h = new Harness();
        var rec = h.Record(h.SameBranchEmployeeId, h.BranchA, TenantA);

        var result = await h.AsManager.Delete(rec.Id);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(h.Exists(rec.Id));
    }

    [Fact]
    public async Task But_not_for_a_worker_in_a_branch_they_do_not_manage()
    {
        using var h = new Harness();
        var rec = h.Record(h.OtherBranchEmployeeId, h.BranchB, TenantA);

        AssertForbidden(await h.AsManager.Delete(rec.Id));
        Assert.True(h.Exists(rec.Id));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("manager")]
    public async Task And_never_for_a_privileged_account_even_in_their_own_branch(string who)
    {
        // The 2026-08-08 rule: branch membership alone is not authority over somebody. Erasing an
        // admin's attendance is a smaller prize than resetting their PIN, but it is the same door.
        using var h = new Harness();
        var target = who == "admin" ? h.SameBranchAdminId : h.SameBranchManagerId;
        var rec = h.Record(target, h.BranchA, TenantA);

        AssertForbidden(await h.AsManager.Delete(rec.Id));
        Assert.True(h.Exists(rec.Id));
    }

    [Fact]
    public async Task Another_tenants_record_is_not_found_at_all_rather_than_refused()
    {
        // Fail-closed multi-tenancy: the global query filter means the row is not visible to look at,
        // so the answer is "no such record" and nothing leaks about another company's data.
        using var h = new Harness();
        var rec = h.Record(h.OtherTenantEmployeeId, h.BranchA, TenantB);

        var result = await h.AsAdmin.Delete(rec.Id);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.True(h.Exists(rec.Id));
    }

    [Fact]
    public async Task The_selfie_goes_with_the_record()
    {
        // A face photo whose record no longer exists is an orphan nobody can account for.
        using var h = new Harness();
        var rec = h.Record(h.SameBranchEmployeeId, h.BranchA, TenantA, photoKey: "checkins/2026/09/01/x.webp");

        await h.AsAdmin.Delete(rec.Id);

        Assert.Equal(new[] { "checkins/2026/09/01/x.webp" }, h.Photo.Deleted);
    }

    [Fact]
    public async Task Deleting_writes_an_audit_line_that_says_what_was_removed()
    {
        // Once the row is gone this is the only place that says a day existed — so it has to carry the
        // times, not merely the id of something that no longer resolves to anything.
        using var h = new Harness();
        var rec = h.Record(h.SameBranchEmployeeId, h.BranchA, TenantA);

        await h.AsAdmin.Delete(rec.Id);

        var log = Assert.Single(h.Db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EmployeeId == h.SameBranchEmployeeId).ToList());
        Assert.Contains("Deleted by", log.Reason);
        Assert.Contains("2026-09-01", log.Reason);
        Assert.Contains("02:46", log.Reason);
    }

    [Fact]
    public async Task A_record_that_is_not_there_is_a_404_not_a_crash()
    {
        using var h = new Harness();
        Assert.IsType<NotFoundObjectResult>(await h.AsAdmin.Delete(Guid.NewGuid()));
    }
}
