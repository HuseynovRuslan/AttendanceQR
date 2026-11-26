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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Deleting a person or a branch against the rows that hold them in place (Restrict foreign keys).
///
/// The in-memory provider enforces no keys, which is how every test stayed green while production refused
/// the deletes. So these check what Postgres would check: after a delete, NO row under any holding key
/// (read from the model) still names the deleted id. And they pin the other half of the bug — a forced
/// delete that the database refuses must leave everything as it was, not the person standing with their
/// attendance already wiped.
/// </summary>
public class DeleteReferencesTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000d7");

    /// <summary>The database saying no: a 23503 naming the table that still holds the row. Off until armed,
    /// so the seeding saves.</summary>
    private sealed class DatabaseRefusal : SaveChangesInterceptor
    {
        public string? Table { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
            => Table is null
                ? ValueTask.FromResult(result)
                : throw new DbUpdateException("simulated: 23503 foreign key violation",
                    new PostgresException("update or delete violates foreign key constraint", "ERROR", "ERROR",
                        PostgresErrorCodes.ForeignKeyViolation, tableName: Table));
    }

    private sealed class Photos : IPhotoStorageService
    {
        public List<string> Deleted { get; } = new();
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
        {
            Deleted.AddRange(keys);
            return Task.FromResult(keys.Count);
        }
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("checkins/k.jpg");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("reference/k.jpg");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("avatars/k.jpg");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("fieldwork/k.jpg");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult($"https://r2/{key}");
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public Photos Photos { get; } = new();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid PersonId { get; } = Guid.NewGuid();
        public Guid Branch { get; } = Guid.NewGuid();
        public Guid OtherBranch { get; } = Guid.NewGuid();
        public Guid RoleId { get; }
        public DatabaseRefusal Refusal { get; } = new();

        public World()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"delete-refs-{Guid.NewGuid()}")
                .AddInterceptors(Refusal)
                .Options, tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(BranchRow(Branch, "Filial"));
            Db.Locations.Add(BranchRow(OtherBranch, "Başqa filial"));
            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, OtherBranch));
            Db.Employees.Add(Person(PersonId, "Silinən", EmployeeRole.Employee, OtherBranch));
            var role = new AccessRole { TenantId = TenantId, Name = "Filial meneceri" };
            Db.AccessRoles.Add(role);
            RoleId = role.Id;
            Db.SaveChanges();
        }

        private static Location BranchRow(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15,
            QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Email = $"{id:N}@t.local", LocationId = locationId,
            Role = role, IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "x",
        };

        public AdminController Admin() => new(
            Db, Options.Create(new InvitationOptions()), new PasswordHasher(),
            new MemoryCacheLoginLockoutStore(new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
            new AppOptions { TimeZone = "Asia/Baku" }, Photos, NullLogger<AdminController>.Instance)
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

        public AdminLocationsController Locations() => new(
            Db,
            new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-for-delete-references", TtlSeconds = 60 })),
            new OffFaceMatch(),
            NullLogger<AdminLocationsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        /// <summary>Everything that can hold a person: a scan with a selfie, the day's summary, a missed
        /// check-out on that scan, a phone change they asked for and one they approved for the admin, role
        /// rows (one revoked, as demotions leave them), a PIN reset they asked for and one they resolved.</summary>
        public (Guid Reviewed, Guid Resolved) GiveThePersonEverything()
        {
            var day = new DateOnly(2026, 11, 20);
            var recordId = Guid.NewGuid();
            Db.AttendanceRecords.Add(new AttendanceRecord
            {
                Id = recordId, TenantId = TenantId, EmployeeId = PersonId, LocationId = OtherBranch,
                AttendanceDate = day, CheckInAtUtc = DateTime.UtcNow, CheckInPhotoKey = $"checkins/{recordId}.jpg",
            });
            Db.DailySummaries.Add(new DailySummary
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = PersonId, LocationId = OtherBranch,
                SummaryDate = day, Status = DailySummaryStatus.Incomplete,
            });
            Db.MissedCheckoutRequests.Add(new MissedCheckoutRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = PersonId, AttendanceRecordId = recordId,
                AttendanceDate = day, RequestedCheckOutAtUtc = DateTime.UtcNow, Reason = "unutdum", RequestedAtUtc = DateTime.UtcNow,
            });
            Db.DeviceChangeRequests.Add(new DeviceChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = PersonId, NewDeviceFingerprint = "fp-own",
                RequestedAtUtc = DateTime.UtcNow,
            });
            var reviewed = new DeviceChangeRequest
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = AdminId, NewDeviceFingerprint = "fp-admin",
                RequestedAtUtc = DateTime.UtcNow, Status = DeviceChangeStatus.Approved, ReviewedByEmployeeId = PersonId,
            };
            Db.DeviceChangeRequests.Add(reviewed);
            Db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = TenantId, EmployeeId = PersonId, AccessRoleId = RoleId, Scope = AccessScopeType.Branch,
                LocationId = OtherBranch, RevokedAtUtc = DateTime.UtcNow,
            });
            Db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = TenantId, EmployeeId = PersonId, AccessRoleId = RoleId, Scope = AccessScopeType.Company,
            });
            Db.PinResetRequests.Add(new PinResetRequest { TenantId = TenantId, EmployeeId = PersonId });
            var resolved = new PinResetRequest
            {
                TenantId = TenantId, EmployeeId = AdminId, Status = PinResetStatus.Resolved,
                ResolvedByEmployeeId = PersonId, ResolvedAtUtc = DateTime.UtcNow,
            };
            Db.PinResetRequests.Add(resolved);
            Db.SaveChanges();
            return (reviewed.Id, resolved.Id);
        }

        /// <summary>What Postgres would refuse over: rows under a holding key that still name <paramref name="id"/>.</summary>
        public async Task<List<string>> StillHolding(Type principal, Guid id)
        {
            var left = new List<string>();
            foreach (var reference in RowReferences.Holding(Db.Model, principal))
            {
                if (await RowReferences.CountAsync(Db, reference, id) > 0)
                    left.Add(reference.Name);
            }
            return left;
        }

        public void Dispose() => Db.Dispose();
    }

    private static object? Prop(IActionResult result, string name)
        => (result as ObjectResult)?.Value?.GetType().GetProperty(name)?.GetValue((result as ObjectResult)!.Value);

    private static int StatusOf(IActionResult result) => (result as ObjectResult)?.StatusCode ?? 0;

    // --- a person -------------------------------------------------------------------------------

    [Fact]
    public async Task History_is_asked_about_by_kind_and_nothing_is_touched_until_the_admin_says_yes()
    {
        using var w = new World();
        w.GiveThePersonEverything();

        var result = await w.Admin().Delete(w.PersonId);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal("EmployeeHasHistory", Prop(result, "error"));
        Assert.Equal(3, Prop(result, "attendance"));   // the scan, its summary, the missed check-out
        Assert.Equal(2, Prop(result, "requests"));     // the phone change they asked for, the one they approved
        Assert.True(await w.Db.Employees.AnyAsync(e => e.Id == w.PersonId));
        Assert.True(await w.Db.RoleAssignments.AnyAsync(a => a.EmployeeId == w.PersonId));
        Assert.Empty(w.Photos.Deleted);
    }

    [Fact]
    public async Task A_forced_delete_leaves_no_row_the_database_would_refuse_over()
    {
        using var w = new World();
        var (reviewed, resolved) = w.GiveThePersonEverything();

        var result = await w.Admin().Delete(w.PersonId, force: true);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await w.Db.Employees.AnyAsync(e => e.Id == w.PersonId));
        Assert.Empty(await w.StillHolding(typeof(Employee), w.PersonId));

        // Someone else's history that only named them stays, unnamed.
        Assert.Null((await w.Db.DeviceChangeRequests.SingleAsync(r => r.Id == reviewed)).ReviewedByEmployeeId);
        Assert.Null((await w.Db.PinResetRequests.SingleAsync(r => r.Id == resolved)).ResolvedByEmployeeId);
        // The forced path used bulk statements the in-memory provider cannot run, so this was never tested:
        // the check-in selfie goes too.
        Assert.Contains(w.Photos.Deleted, k => k.StartsWith("checkins/"));
    }

    [Fact]
    public async Task A_forced_delete_the_database_refuses_changes_nothing_and_says_what_holds_them()
    {
        // Production, before: the history went in statements that committed one by one, then the person's
        // own DELETE was refused — the person stayed, their past days were gone, and a retry no longer even
        // asked. Now it is one save: refused means nothing moved, and the admin is told which table.
        using var w = new World();
        w.GiveThePersonEverything();
        w.Refusal.Table = "SomethingNew";

        var result = await w.Admin().Delete(w.PersonId, force: true);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal("EmployeeReferenced", Prop(result, "error"));
        Assert.Equal(new[] { "SomethingNew" }, Prop(result, "tables"));
        Assert.True(await w.Db.Employees.AnyAsync(e => e.Id == w.PersonId));
        Assert.True(await w.Db.AttendanceRecords.AnyAsync(a => a.EmployeeId == w.PersonId));
        Assert.True(await w.Db.DailySummaries.AnyAsync(d => d.EmployeeId == w.PersonId));
        // No photo is removed for a person who is still there.
        Assert.Empty(w.Photos.Deleted);
    }

    // --- a branch -------------------------------------------------------------------------------

    private static void AddGrant(World w, Guid? createdBy, DateTime? revokedAt = null)
    {
        w.Db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = TenantId, EmployeeId = w.PersonId, AccessRoleId = w.RoleId, Scope = AccessScopeType.Branch,
            LocationId = w.Branch, CreatedByEmployeeId = createdBy, RevokedAtUtc = revokedAt,
        });
        w.Db.SaveChanges();
    }

    [Fact]
    public async Task A_branch_once_ticked_for_a_manager_still_deletes_and_takes_the_mirrors_with_it()
    {
        // The access model mirrors each «manages this branch» as a system grant and only ever revokes it.
        // ManagedLocations go with the branch by cascade; their mirrors used to hold it for ever — a 500.
        using var w = new World();
        AddGrant(w, createdBy: null);
        AddGrant(w, createdBy: null, revokedAt: DateTime.UtcNow);

        var result = await w.Locations().Delete(w.Branch);

        Assert.IsType<OkObjectResult>(result);
        Assert.False(await w.Db.Locations.AnyAsync(l => l.Id == w.Branch));
        Assert.Empty(await w.StillHolding(typeof(Location), w.Branch));
    }

    [Fact]
    public async Task A_grant_a_person_made_holds_the_branch_and_the_refusal_says_so()
    {
        // Revoked too: «who could act here on the 3rd» is the record. The answer is to deactivate the branch.
        using var w = new World();
        AddGrant(w, createdBy: w.AdminId, revokedAt: DateTime.UtcNow);
        AddGrant(w, createdBy: null);

        var result = await w.Locations().Delete(w.Branch);

        Assert.Equal("LocationInUse", Prop(result, "error"));
        Assert.Equal(1, Prop(result, "accessCount"));
        Assert.True(await w.Db.Locations.AnyAsync(l => l.Id == w.Branch));
        // Refused means untouched — the system's mirror is still there as well.
        Assert.Equal(2, await w.Db.RoleAssignments.CountAsync(a => a.LocationId == w.Branch));
    }

    [Fact]
    public async Task Approval_areas_and_requests_hold_the_branch_and_are_counted()
    {
        // A rejected «new employee» request for this branch is a record of a decision; so is the area.
        using var w = new World();
        w.Db.ApprovalAreas.Add(new ApprovalArea
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Scope = AccessScopeType.Branch, LocationId = w.Branch,
            ActivatedAtUtc = DateTime.UtcNow, ActivatedByEmployeeId = w.AdminId,
        });
        w.Db.ChangeRequests.Add(new ChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LocationId = w.Branch, RequestedByEmployeeId = w.AdminId,
            CreatedAtUtc = DateTime.UtcNow,
        });
        w.Db.SaveChanges();

        var result = await w.Locations().Delete(w.Branch);

        Assert.Equal("LocationInUse", Prop(result, "error"));
        Assert.Equal(2, Prop(result, "approvalCount"));
        Assert.True(await w.Db.Locations.AnyAsync(l => l.Id == w.Branch));
    }

    [Fact]
    public async Task A_branch_delete_the_database_refuses_is_a_409_that_names_the_table_not_a_500()
    {
        using var w = new World();
        w.Refusal.Table = "SomethingNew";

        var result = await w.Locations().Delete(w.Branch);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.Equal("LocationInUse", Prop(result, "error"));
        Assert.Equal(new List<string> { "SomethingNew" }, Prop(result, "otherTables"));
        Assert.True(await w.Db.Locations.AnyAsync(l => l.Id == w.Branch));
    }
}
