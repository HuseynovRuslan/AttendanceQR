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
/// The reference photo is the face every check-in selfie — and the forgot-PIN face check — is judged
/// against. POST /api/attendance/me/reference-photo replaced it unconditionally and wrote nothing down,
/// so a session held for a minute by somebody else was enough to make THEIR face this person's from then
/// on. Its one legitimate caller is the first sign-in of a temp-PIN account that has no reference yet.
///
/// Now: set only where empty (409 ReferencePhotoAlreadySet otherwise), every set and every admin clear on
/// the audit log. A wrong reference is fixed the audited way — an admin clears it, the next set fills it.
/// </summary>
public class ReferencePhotoOverwriteTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e9");
    private static readonly string Photo = "data:image/jpeg;base64," + Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public RecordingPhoto Storage { get; } = new();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();

        public Harness(string? existingReference)
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"reference-photo-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Employees.Add(Person(EmployeeId, EmployeeRole.Employee, existingReference));
            Db.Employees.Add(Person(AdminId, EmployeeRole.Admin, "reference/admin.jpg"));
            Db.SaveChanges();
        }

        private Employee Person(Guid id, EmployeeRole role, string? reference) => new()
        {
            Id = id, TenantId = TenantId, FullName = role.ToString(), Role = role, LocationId = LocationId,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow.AddDays(-5), PasswordHash = "x",
            ReferencePhotoKey = reference,
            ReferencePhotoTakenAtUtc = reference is null ? null : DateTime.UtcNow.AddDays(-5),
        };

        private static ControllerContext As(Guid id, EmployeeRole role) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", id.ToString()), new Claim("role", role.ToString())], "test")),
            },
        };

        public AttendanceController Self() => new(
            Db, new QrTokenService(Options.Create(new QrTokenOptions { Secret = "reference-photo-test-secret-key-0123", TtlSeconds = 300 })),
            new AttendanceQueryService(Db), Storage, new StubQueue(), new PhotoUploadQueue(),
            new StubFace(), new DeviceBindingOptions(), new AppOptions { TimeZone = "Asia/Baku" },
            new MemoryCache(new MemoryCacheOptions()), NullLogger<AttendanceController>.Instance)
        {
            ControllerContext = As(EmployeeId, EmployeeRole.Employee),
        };

        public AdminController Admin() => new(
            Db, Options.Create(new InvitationOptions()), new StubHasher(), new StubLockout(), new AppOptions(),
            Storage, NullLogger<AdminController>.Instance)
        {
            ControllerContext = As(AdminId, EmployeeRole.Admin),
        };

        public Employee Row(Guid id) => Db.Employees.AsNoTracking().Single(e => e.Id == id);

        public List<AuditLog> ReferenceAudits() => Db.AuditLogs.AsNoTracking()
            .Where(a => a.EventType == AuditEventType.ReferencePhotoChanged).ToList();

        public void Dispose() => Db.Dispose();
    }

    internal sealed class RecordingPhoto : IPhotoStorageService
    {
        public int ReferenceUploads { get; private set; }
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default)
        {
            ReferenceUploads++;
            return Task.FromResult($"reference/{e}");
        }
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

    private sealed class StubHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class StubLockout : ILoginLockoutStore
    {
        public LoginAttempt BeginAttempt(string key) => new(true, 5, 0);
        public bool IsLockedOut(string key) => false;
        public void RecordSuccess(string key) { }
    }

    [Fact]
    public async Task A_first_sign_in_sets_the_missing_reference_and_it_is_on_the_record()
    {
        using var h = new Harness(existingReference: null);

        Assert.IsType<OkObjectResult>(await h.Self().SetMyReferencePhoto(new ReferencePhotoRequest(Photo)));

        Assert.Equal($"reference/{h.EmployeeId}", h.Row(h.EmployeeId).ReferencePhotoKey);
        var audit = Assert.Single(h.ReferenceAudits());
        Assert.Equal(h.EmployeeId, audit.EmployeeId);
    }

    [Fact]
    public async Task An_existing_reference_is_not_replaced_from_the_session()
    {
        // The overwrite: whoever holds the session puts their face in, and from then on THEY are this person
        // to every check-in and to the forgot-PIN face check.
        using var h = new Harness(existingReference: "reference/the-real-face.jpg");

        var refused = Assert.IsType<ConflictObjectResult>(
            await h.Self().SetMyReferencePhoto(new ReferencePhotoRequest(Photo)));

        Assert.Contains("ReferencePhotoAlreadySet", refused.Value!.ToString());
        Assert.Equal("reference/the-real-face.jpg", h.Row(h.EmployeeId).ReferencePhotoKey);
        Assert.Equal(0, h.Storage.ReferenceUploads);   // not even uploaded beside it
        Assert.Empty(h.ReferenceAudits());
    }

    [Fact]
    public async Task An_admin_clearing_a_reference_is_on_the_record_and_lets_the_next_one_in()
    {
        // The audited way to replace a wrong reference: the admin clears it, naming themselves, and the
        // employee's next set (or first check-in) fills it again.
        using var h = new Harness(existingReference: "reference/the-admins-face.jpg");

        Assert.IsType<OkObjectResult>(await h.Admin().ResetReferencePhoto(h.EmployeeId));

        var cleared = Assert.Single(h.ReferenceAudits());
        Assert.Equal(h.EmployeeId, cleared.EmployeeId);
        Assert.Contains(h.AdminId.ToString(), cleared.Reason);

        Assert.IsType<OkObjectResult>(await h.Self().SetMyReferencePhoto(new ReferencePhotoRequest(Photo)));
        Assert.Equal($"reference/{h.EmployeeId}", h.Row(h.EmployeeId).ReferencePhotoKey);
        Assert.Equal(2, h.ReferenceAudits().Count);
    }

    [Fact]
    public async Task Clearing_everyones_reference_at_once_leaves_one_row_naming_the_admin_and_the_count()
    {
        using var h = new Harness(existingReference: "reference/x.jpg");

        var ok = Assert.IsType<OkObjectResult>(await h.Admin().ResetAllReferencePhotos());

        Assert.Contains("reset = 2", ok.Value!.ToString());
        var row = Assert.Single(h.ReferenceAudits());
        Assert.Equal(h.AdminId, row.EmployeeId);
        Assert.Contains("2", row.Reason);
    }
}
