using System.Security.Claims;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Deleting an employee removes their rows. It used to leave every photograph of them in the bucket.
///
/// The retention job prunes <c>checkins/</c> by age and is explicitly forbidden from touching
/// <c>reference/</c>, so the enrollment selfie — a face, tied to a name — was kept for ever, and the
/// keys that pointed at the rest died with the rows, leaving objects nobody could find to remove.
/// Meanwhile qrlog.az/hesab-silinmesi/ publishes that a deletion request removes "referans (profil)
/// şəkli və giriş/çıxış anındakı selfilər" within 30 days.
///
/// These pin the part that is easy to break: the keys are read BEFORE the rows are deleted, all three
/// photo homes are covered, and a storage failure never resurrects the employee.
/// </summary>
public class EmployeeDeletePurgesPhotosTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    private sealed class RecordingPhotoStorage : IPhotoStorageService
    {
        public List<string> Deleted { get; } = new();
        public bool Throw { get; set; }

        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("R2 unreachable");
            Deleted.AddRange(keys);
            return Task.FromResult(keys.Count);
        }

        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("checkins/k.jpg");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("reference/k.jpg");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("fieldwork/k.jpg");
        public Task<string> UploadTaskPhotoAsync(Guid t, Guid id, byte[] b, CancellationToken ct = default) => Task.FromResult("tasks/k.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult($"https://r2/{key}");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminController Controller { get; }
        public RecordingPhotoStorage Photos { get; } = new();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid LocationId { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"del-{Guid.NewGuid()}").Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(new Location
            {
                Id = LocationId, TenantId = TenantId, Name = "Filial", Latitude = 40.4, Longitude = 49.8,
                RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
            });
            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, referenceKey: null));
            Db.Employees.Add(Person(EmployeeId, "Silinən İşçi", EmployeeRole.Employee,
                referenceKey: $"reference/{EmployeeId}.jpg"));
            Db.SaveChanges();

            Controller = new AdminController(
                Db,
                Options.Create(new InvitationOptions()),
                new PasswordHasher(),
                new MemoryCacheLoginLockoutStore(new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
                new AppOptions { TimeZone = "Asia/Baku" },
                Photos,
                NullLogger<AdminController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", AdminId.ToString()),
                            new Claim("role", nameof(EmployeeRole.Admin)),
                        }, "test")),
                    },
                },
            };
        }

        private Employee Person(Guid id, string name, EmployeeRole role, string? referenceKey) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Email = $"{id:N}@t.local",
            LocationId = LocationId, Role = role, IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x", ReferencePhotoKey = referenceKey,
        };

        /// <summary>An attendance row with a check-in selfie — this is what makes a delete "forced".</summary>
        public void GiveThemAttendance()
        {
            var recordId = Guid.NewGuid();
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = recordId, TenantId = TenantId, EmployeeId = EmployeeId, LocationId = LocationId,
                AttendanceDate = DateOnly.FromDateTime(DateTime.UtcNow), CheckInAtUtc = DateTime.UtcNow,
                CheckInPhotoKey = $"checkins/2026/08/10/{EmployeeId}/{recordId}.jpg",
            });
            Db.SaveChanges();
        }

        /// <summary>A completed field visit: two selfies and a photo of the work.
        /// Field visits are NOT part of the "has history" gate, so this deletes without force.</summary>
        public Guid GiveThemAFieldVisit()
        {
            var visit = new FieldVisit
            {
                TenantId = TenantId, EmployeeId = EmployeeId,
                VisitDate = DateOnly.FromDateTime(DateTime.UtcNow), Status = FieldVisitStatus.Completed,
                CheckInPhotoKey = "fieldvisits/in.jpg",
                CheckOutPhotoKey = "fieldvisits/out.jpg",
                WorkPhotoKey = "fieldwork/work.jpg",
            };
            Db.FieldVisits.Add(visit);
            Db.SaveChanges();
            return visit.Id;
        }

        public void Dispose() => Db.Dispose();
    }

    [Fact]
    public async Task Deleting_an_employee_with_no_history_still_removes_their_face()
    {
        // The plain case, and the one that leaked for ever: no attendance, so nothing the retention
        // job would ever have swept — just an enrollment selfie under a prefix it is forbidden to touch.
        using var h = new Harness();

        await h.Controller.Delete(h.EmployeeId);

        Assert.Equal(new[] { $"reference/{h.EmployeeId}.jpg" }, h.Photos.Deleted);
        Assert.False(await h.Db.Employees.AnyAsync(e => e.Id == h.EmployeeId));
    }

    [Fact]
    public async Task Field_visit_selfies_and_work_photo_go_too()
    {
        // The other two homes. A field visit's check-in/check-out selfies are faces exactly like the
        // poster ones; the work photo is evidence of a job rather than of a person, but it is filed
        // under this employee's visit and nothing else will ever come looking for it.
        using var h = new Harness();
        var visitId = h.GiveThemAFieldVisit();

        var result = await h.Controller.Delete(h.EmployeeId);

        Assert.Equal(4, h.Photos.Deleted.Count);
        Assert.Contains($"reference/{h.EmployeeId}.jpg", h.Photos.Deleted);
        Assert.Contains("fieldvisits/in.jpg", h.Photos.Deleted);
        Assert.Contains("fieldvisits/out.jpg", h.Photos.Deleted);
        Assert.Contains("fieldwork/work.jpg", h.Photos.Deleted);
        Assert.Equal(4, Convert.ToInt32(Prop(result, "photosDeleted")));

        // FieldVisit has no FK to Employee, so the row outlives the person. Its keys must not: they
        // now point at deleted objects, and the field-visit screens would spend a presign call each
        // on a 404.
        var visit = await h.Db.FieldVisits.FirstAsync(v => v.Id == visitId);
        Assert.Null(visit.CheckInPhotoKey);
        Assert.Null(visit.CheckOutPhotoKey);
        Assert.Null(visit.WorkPhotoKey);
    }

    [Fact]
    public async Task An_employee_with_history_is_still_refused_without_force_and_keeps_their_photos()
    {
        // The guard runs first. Nothing is deleted, so nothing may be purged either — a rejected
        // request that had already emptied the bucket would be the worst of both.
        using var h = new Harness();
        h.GiveThemAttendance();

        var result = await h.Controller.Delete(h.EmployeeId);

        Assert.Equal("EmployeeHasHistory", Error(result));
        Assert.Empty(h.Photos.Deleted);
        Assert.True(await h.Db.Employees.AnyAsync(e => e.Id == h.EmployeeId));
    }

    // NOTE: the force=true path (an employee WITH attendance) is not covered here. It runs
    // ExecuteDeleteAsync, which the EF Core InMemory provider does not implement — the check-in
    // selfie keys are collected by the same block these tests do exercise, immediately above that
    // call, so what is untested is the deletion of the rows, not the gathering of the keys.

    [Fact]
    public async Task Storage_being_down_does_not_undo_the_deletion()
    {
        // Object storage has no transaction to join. The row is already gone by the time the purge
        // runs, and the admin asked for a deletion — failing the request here would report "not
        // deleted" about an employee who is, in fact, deleted. It is logged instead.
        using var h = new Harness();
        h.Photos.Throw = true;

        var result = await h.Controller.Delete(h.EmployeeId);

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        Assert.False(await h.Db.Employees.AnyAsync(e => e.Id == h.EmployeeId));
        Assert.Equal(0, Convert.ToInt32(Prop(result, "photosDeleted")));
    }

    [Fact]
    public async Task Deleting_yourself_is_still_refused_before_anything_is_purged()
    {
        using var h = new Harness();

        var result = await h.Controller.Delete(h.AdminId);

        Assert.Equal("CannotDeleteSelf", Error(result));
        Assert.Empty(h.Photos.Deleted);
    }

    // --- reading the anonymous action results --------------------------------

    private static object? Prop(IActionResult result, string name)
    {
        var value = result switch
        {
            OkObjectResult ok => ok.Value,
            ObjectResult obj => obj.Value,
            _ => null,
        };
        return value?.GetType().GetProperty(name)?.GetValue(value);
    }

    private static string? Error(IActionResult result) => Prop(result, "error") as string;

    private static int StatusCode(IActionResult result) => result switch
    {
        ObjectResult obj => obj.StatusCode ?? StatusCodes.Status200OK,
        _ => 0,
    };
}
