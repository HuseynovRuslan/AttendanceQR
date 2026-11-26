using System.Security.Claims;
using System.Text.Json;
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
/// The «Filial» column of a staff spreadsheet names a branch, and a company can have two branches of that name —
/// most often a deactivated one and the new one that replaced it. The import took the first match in whatever
/// order the database returned the branches, which on Postgres is no order at all: new staff landed in the closed
/// branch one day and the open one the next, and the response said nothing either way.
///
/// The in-memory provider returns rows in the order they were added, so each test adds the branch that must NOT
/// win first — the answer the old lookup gave here, and one Postgres was free to give.
/// </summary>
public class BulkImportBranchNameTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000b1a7");

    private sealed class World : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminController Admin { get; }
        public Guid Batch { get; }

        public World()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"bulk-branch-{Guid.NewGuid()}").Options,
                tenant);
            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Batch = Branch("Baş ofis");
            Db.SaveChanges();

            Admin = new AdminController(
                Db,
                Options.Create(new InvitationOptions()),
                new PasswordHasher(),
                new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())),
                new AppOptions { TimeZone = "Asia/Baku" },
                new NoPhotos(),
                NullLogger<AdminController>.Instance)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(
                            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", nameof(EmployeeRole.Admin))], "test")),
                    },
                },
            };
        }

        public Guid Branch(string name, bool active = true)
        {
            var location = new Location
            {
                TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), IsActive = active,
            };
            Db.Locations.Add(location);
            Db.SaveChanges();
            return location.Id;
        }

        public Guid? BranchOf(string fullName) =>
            Db.Employees.AsNoTracking().Where(e => e.FullName == fullName).Select(e => (Guid?)e.LocationId).SingleOrDefault();

        public void Dispose() => Db.Dispose();
    }

    private sealed class NoPhotos : IPhotoStorageService
    {
        public Task<string> UploadCheckInPhotoAsync(Guid e, Guid r, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadReferencePhotoAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadAvatarAsync(Guid e, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> UploadFieldWorkPhotoAsync(Guid t, Guid v, byte[] b, CancellationToken ct = default) => Task.FromResult("k");
        public Task<string> GetPresignedUrlAsync(string key, CancellationToken ct = default) => Task.FromResult(key);
        public Task<byte[]> GetBytesAsync(string key, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
        public Task DeleteByPrefixOlderThanAsync(string p, DateTime o, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> DeleteObjectsAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default) => Task.FromResult(keys.Count);
    }

    /// <summary>The row errors of a bulk response, as (fullName, error).</summary>
    private static List<(string Name, string Error)> Failed(IActionResult result)
    {
        var body = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);
        return body.GetProperty("failed").EnumerateArray()
            .Select(f => (f.GetProperty("fullName").GetString()!, f.GetProperty("error").GetString()!))
            .ToList();
    }

    [Fact]
    public async Task A_named_branch_is_the_open_one_not_the_deactivated_one_of_the_same_name()
    {
        using var w = new World();
        var closed = w.Branch("Mərkəz", active: false);
        var open = w.Branch(" mərkəz ");

        var result = await w.Admin.BulkImport(new BulkInviteRequest(w.Batch, EmployeeRole.Employee,
        [
            new BulkInviteRow("Yeni İşçi", PhoneNumber: "+994552230001", LocationName: "MƏRKƏZ"),
        ]));

        Assert.Empty(Failed(result));
        Assert.Equal(open, w.BranchOf("Yeni İşçi"));
        Assert.NotEqual(closed, w.BranchOf("Yeni İşçi"));
    }

    [Fact]
    public async Task A_name_two_open_branches_share_fails_that_row_and_the_rest_still_import()
    {
        using var w = new World();
        w.Branch("Sahil");
        w.Branch("Sahil");
        var other = w.Branch("Dəniz");

        var result = await w.Admin.BulkImport(new BulkInviteRequest(w.Batch, EmployeeRole.Employee,
        [
            new BulkInviteRow("Birinci İşçi", PhoneNumber: "+994552230011", LocationName: "Sahil"),
            new BulkInviteRow("İkinci İşçi", PhoneNumber: "+994552230012", LocationName: "Dəniz"),
            new BulkInviteRow("Üçüncü İşçi", PhoneNumber: "+994552230013"),
        ]));

        Assert.Equal([("Birinci İşçi", "LocationAmbiguous")], Failed(result));
        Assert.Null(w.BranchOf("Birinci İşçi"));
        Assert.Equal(other, w.BranchOf("İkinci İşçi"));
        Assert.Equal(w.Batch, w.BranchOf("Üçüncü İşçi"));
    }

    [Fact]
    public async Task The_invite_link_import_reads_the_column_by_the_same_rule()
    {
        using var w = new World();
        w.Branch("Sahil", active: false);
        var open = w.Branch("Sahil");
        w.Branch("Liman");
        w.Branch("Liman");

        var result = await w.Admin.BulkInvite(new BulkInviteRequest(w.Batch, EmployeeRole.Employee,
        [
            new BulkInviteRow("Birinci İşçi", PhoneNumber: "+994552230021", LocationName: "Sahil"),
            new BulkInviteRow("İkinci İşçi", PhoneNumber: "+994552230022", LocationName: "Liman"),
        ]));

        Assert.Equal([("İkinci İşçi", "LocationAmbiguous")], Failed(result));
        Assert.Equal(open, w.BranchOf("Birinci İşçi"));
    }

    [Fact]
    public async Task A_deactivated_branch_named_on_purpose_is_still_found_when_it_is_the_only_one()
    {
        using var w = new World();
        var closed = w.Branch("Köhnə anbar", active: false);

        var result = await w.Admin.BulkImport(new BulkInviteRequest(w.Batch, EmployeeRole.Employee,
        [
            new BulkInviteRow("Anbardar", PhoneNumber: "+994552230031", LocationName: "Köhnə anbar"),
        ]));

        Assert.Empty(Failed(result));
        Assert.Equal(closed, w.BranchOf("Anbardar"));
    }

    [Fact]
    public void The_answer_does_not_depend_on_the_order_the_branches_arrive_in()
    {
        var closed = (Guid.NewGuid(), "Mərkəz", false);
        var open = (Guid.NewGuid(), "Mərkəz", true);
        var twinA = (Guid.NewGuid(), "Sahil", true);
        var twinB = (Guid.NewGuid(), "Sahil", true);
        var oldA = (Guid.NewGuid(), "Anbar", false);
        var oldB = (Guid.NewGuid(), "Anbar", false);
        var all = new[] { closed, open, twinA, twinB, oldA, oldB };

        foreach (var order in new[] { all, Enumerable.Reverse(all).ToArray(), [open, twinB, oldA, closed, oldB, twinA] })
        {
            var byName = AdminController.BranchesByName(order);
            Assert.Equal(open.Item1, byName["mərkəz"]);
            Assert.Null(byName["SAHIL"]);
            Assert.Null(byName["Anbar"]);   // two closed branches and no open one: still a tie, still refused
        }
    }
}
