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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The roster query, compiled against the REAL PostgreSQL provider.
///
/// The rest of the suite runs on EF Core InMemory, which happily executes LINQ that Npgsql cannot
/// translate at all — so a query that works in every other test can still 500 every request to
/// /admin/employees in production. <c>ToQueryString()</c> does the translation without opening a
/// connection, which is the whole point: it fails here, on a laptop, instead of on the Employees page
/// in front of an admin.
///
/// Four things in this query are exactly the kind that fall back to client evaluation or refuse to
/// translate: the Azerbaijani ICU collation, <c>Guid.ToString().Contains(...)</c> for the id-prefix
/// search, the correlated EXISTS/COUNT subqueries that replaced <c>.Include(e => e.DeviceBindings)</c>,
/// and OFFSET/LIMIT. If any of them is evaluated in the browser's place — in the application, over
/// the whole table — the fix this file guards has been undone without a single failing test.
/// </summary>
public class EmployeeListQueryTranslationTests
{
    /// <summary>
    /// A controller over a Npgsql context that is never connected to. ToQueryString only needs the
    /// provider's SQL generator, not a server.
    /// </summary>
    private static AdminController Controller(out AppDbContext db)
    {
        var tenant = new TenantContext();
        tenant.Resolve(Guid.NewGuid());
        db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none")
                .Options,
            tenant);

        return new AdminController(
            db,
            Options.Create(new InvitationOptions()),
            new PasswordHasher(),
            new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())),
            new AppOptions { TimeZone = "Asia/Baku", HiddenEmails = "root@qrlog.az" },
            new NoPhotos(),
            NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", nameof(EmployeeRole.Admin))],
                        "test")),
                },
            },
        };
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

    [Fact]
    public void Every_filter_the_screen_offers_reaches_the_database()
    {
        var admin = Controller(out var db);
        using (db)
        {
            var sql = admin.FilteredEmployees(
                search: "həsən", status: "notstarted", locationId: Guid.NewGuid(), role: "Employee", showLeft: false)
                .ToQueryString();

            // Multi-tenancy is fail-closed; the global filter must survive every rewrite of this query.
            Assert.Contains("TenantId", sql);
            Assert.Contains("IsActive", sql);
            Assert.Contains("LocationId", sql);
            Assert.Contains("Role", sql);
            // The four search fields, lowered in SQL rather than in the application.
            Assert.Contains("lower", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("FullName", sql);
            Assert.Contains("PhoneNumber", sql);
            Assert.Contains("Position", sql);
            // «Tətbiqi açmayıb» — never activated, or still on an admin-issued PIN.
            Assert.Contains("ActivatedAtUtc", sql);
            Assert.Contains("MustChangePin", sql);
        }
    }

    [Fact]
    public void The_id_prefix_search_translates_rather_than_scanning_in_memory()
    {
        // The list prints an eight-character id prefix and people paste it back in. Guid.ToString()
        // inside a Contains is the single most likely thing here to fall out of SQL.
        var admin = Controller(out var db);
        using (db)
        {
            var sql = admin.FilteredEmployees("a1b2c3d4", null, null, null, false).ToQueryString();

            Assert.Contains("Id", sql);
            Assert.Contains("a1b2c3d4", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_nopush_and_nodevice_filters_become_subqueries_not_table_loads()
    {
        var admin = Controller(out var db);
        using (db)
        {
            var noPush = admin.FilteredEmployees(null, "nopush", null, null, false).ToQueryString();
            var noDevice = admin.FilteredEmployees(null, "nodevice", null, null, false).ToQueryString();

            Assert.Contains("PushSubscriptions", noPush);
            Assert.Contains("EXISTS", noPush, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("DeviceBindings", noDevice);
            Assert.Contains("EXISTS", noDevice, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_azerbaijani_collation_is_applied_by_the_database()
    {
        // Postgres' default collation sorts «Ə», «İ» and «Ç» after «Z», so an alphabetical roster
        // would put a third of Azerbaijani surnames in a block at the end. The browser used to avoid
        // that with localeCompare('az'); moving the ordering server-side means saying it in SQL.
        var admin = Controller(out var db);
        using (db)
        {
            var sql = admin.ByName(admin.FilteredEmployees(null, null, null, null, false)).ToQueryString();

            Assert.Contains("COLLATE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(AdminController.AzCollation, sql);
            // Id breaks ties, or two people with the same name can land on both pages or on neither.
            Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_page_is_cut_by_the_database()
    {
        var admin = Controller(out var db);
        using (db)
        {
            var sql = admin.ByName(admin.FilteredEmployees(null, null, null, null, false))
                .Skip(50).Take(25)
                .ToQueryString();

            Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_device_facts_are_scalars_computed_per_row()
    {
        // The shape that replaced .Include(e => e.DeviceBindings) — which pulled every binding an
        // employee had ever had, to show one label and a count.
        var admin = Controller(out var db);
        using (db)
        {
            var sql = admin.ByName(admin.FilteredEmployees(null, null, null, null, false))
                .Select(e => new
                {
                    e.Id,
                    PushEnabled = db.PushSubscriptions.Any(p => p.EmployeeId == e.Id),
                    DeviceCount = db.DeviceBindings.Count(d => d.EmployeeId == e.Id && d.IsActive),
                    Newest = db.DeviceBindings
                        .Where(d => d.EmployeeId == e.Id && d.IsActive)
                        .OrderByDescending(d => d.LastSeenAtUtc)
                        .Select(d => new { d.DeviceLabel, d.BoundAtUtc })
                        .FirstOrDefault(),
                })
                .ToQueryString();

            Assert.Contains("DeviceBindings", sql);
            Assert.Contains("PushSubscriptions", sql);
            Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase);
        }
    }
}
