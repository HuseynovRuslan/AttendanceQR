using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Putting a whole crew on one shift.
///
/// Assigning a shift had stayed a per-person edit while shifts quietly became the way hours are
/// described — and per-day hours made a shift the ONLY way to say "08:00–18:00, but 09:00 at the
/// weekend". For a real crew that is forty-six passes through a form whose request object
/// null-defaults every field it is not handed, which is a lot of chances to blank a salary.
///
/// The boundary that must not move with it: a shift pinned to a branch belongs to that branch's
/// staff. A bulk action over a filtered list will sometimes include somebody else, so those rows are
/// skipped rather than failing the whole call — the same choice the bulk permission grant makes, for
/// the same reason.
/// </summary>
public class BulkScheduleTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private sealed class Harness : IDisposable
    {
        public AppDbContext Db { get; }
        public AdminController Admin { get; }
        public Guid BranchA { get; } = Guid.NewGuid();
        public Guid BranchB { get; } = Guid.NewGuid();
        public Guid SharedShift { get; } = Guid.NewGuid();
        public Guid BranchAShift { get; } = Guid.NewGuid();

        public Harness()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"bulk-sched-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            foreach (var (id, name) in new[] { (BranchA, "Heydər Əliyev Mərkəzi"), (BranchB, "Dədə Qorqud Parkı") })
                Db.Locations.Add(new Location
                {
                    Id = id, TenantId = TenantId, Name = name,
                    Latitude = 40.4, Longitude = 49.8, RadiusMeters = 150,
                    ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
                    LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
                });

            Db.Schedules.Add(new Schedule
            {
                Id = SharedShift, TenantId = TenantId, Name = "HƏM Təmizlik", LocationId = null,
                ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0),
                WorkDaysMask = 127, DayHours = "0=09:00-18:00;6=09:00-18:00",
            });
            Db.Schedules.Add(new Schedule
            {
                Id = BranchAShift, TenantId = TenantId, Name = "Yalnız HƏM", LocationId = BranchA,
                ShiftStart = new TimeOnly(8, 0), ShiftEnd = new TimeOnly(18, 0), WorkDaysMask = 127,
            });
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
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", Guid.NewGuid().ToString()),
                            new Claim("role", nameof(EmployeeRole.Admin)),
                        }, "test")),
                    },
                },
            };
        }

        public Guid Person(Guid branch, Guid? shift = null, decimal? salary = null)
        {
            var id = Guid.NewGuid();
            Db.Employees.Add(new Employee
            {
                Id = id, TenantId = TenantId, FullName = "X", Role = EmployeeRole.Employee,
                IsActive = true, PasswordHash = "h", LocationId = branch, ScheduleId = shift,
                MonthlySalary = salary,
            });
            Db.SaveChanges();
            return id;
        }

        public Guid? ShiftOf(Guid id) => Db.Employees.AsNoTracking().Single(e => e.Id == id).ScheduleId;

        public Task<IActionResult> Assign(IEnumerable<Guid> ids, Guid? shift)
            => Admin.BulkSchedule(new BulkScheduleRequest(ids.ToList(), shift));

        public void Dispose() => Db.Dispose();
    }



    /// <summary>The controller needs one; nothing here touches a photo.</summary>
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

    private static int Field(IActionResult result, string name)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (int)ok.Value!.GetType().GetProperty(name)!.GetValue(ok.Value)!;
    }

    [Fact]
    public async Task Puts_a_whole_crew_on_one_shift()
    {
        using var h = new Harness();
        var crew = Enumerable.Range(0, 5).Select(_ => h.Person(h.BranchA)).ToList();

        Assert.Equal(5, Field(await h.Assign(crew, h.SharedShift), "changed"));
        Assert.All(crew, id => Assert.Equal(h.SharedShift, h.ShiftOf(id)));
    }

    [Fact]
    public async Task Takes_them_off_again()
    {
        // A shift that can only be switched on is a ratchet. Clearing returns each person to their own
        // hours, or their branch's — the behaviour that predates named shifts.
        using var h = new Harness();
        var id = h.Person(h.BranchA, h.SharedShift);

        Assert.Equal(1, Field(await h.Assign([id], null), "changed"));
        Assert.Null(h.ShiftOf(id));
    }

    [Fact]
    public async Task A_branch_shift_skips_people_from_another_branch()
    {
        // The boundary. "Yalnız HƏM" means nothing at Dədə Qorqud, and a filtered list will sometimes
        // carry one of them.
        using var h = new Harness();
        var mine = h.Person(h.BranchA);
        var theirs = h.Person(h.BranchB);

        var result = await h.Assign([mine, theirs], h.BranchAShift);

        Assert.Equal(1, Field(result, "changed"));
        Assert.Equal(1, Field(result, "skipped"));
        Assert.Equal(h.BranchAShift, h.ShiftOf(mine));
        Assert.Null(h.ShiftOf(theirs));
    }

    [Fact]
    public async Task A_company_wide_shift_reaches_every_branch()
    {
        using var h = new Harness();
        var a = h.Person(h.BranchA);
        var b = h.Person(h.BranchB);

        Assert.Equal(2, Field(await h.Assign([a, b], h.SharedShift), "changed"));
    }

    [Fact]
    public async Task Rows_already_on_that_shift_are_not_counted_as_changed()
    {
        // The number the screen reports back. Counting no-ops would tell an admin they had just moved
        // forty-six people when they moved two.
        using var h = new Harness();
        var already = h.Person(h.BranchA, h.SharedShift);
        var not = h.Person(h.BranchA);

        Assert.Equal(1, Field(await h.Assign([already, not], h.SharedShift), "changed"));
    }

    [Fact]
    public async Task A_shift_from_nowhere_is_refused_outright()
    {
        // Not skipped — refused. An id that resolves to nothing inside this company's query filter is
        // either another company's shift or a mistake, and neither should quietly do half the work.
        using var h = new Harness();
        var id = h.Person(h.BranchA);

        Assert.IsType<BadRequestObjectResult>(await h.Assign([id], Guid.NewGuid()));
        Assert.Null(h.ShiftOf(id));
    }

    [Fact]
    public async Task An_empty_list_is_refused_rather_than_silently_doing_nothing()
    {
        using var h = new Harness();
        Assert.IsType<BadRequestObjectResult>(await h.Assign([], h.SharedShift));
    }

    [Fact]
    public async Task Nothing_but_the_shift_is_touched()
    {
        // The reason a bulk endpoint exists at all: the per-person form's request object null-defaults
        // every field it is not handed, so forty-six passes through it are forty-six chances to blank
        // a salary. This one writes ScheduleId and nothing else.
        using var h = new Harness();
        var id = h.Person(h.BranchA, salary: 700m);

        await h.Assign([id], h.SharedShift);

        var after = h.Db.Employees.AsNoTracking().Single(e => e.Id == id);
        Assert.Equal(700m, after.MonthlySalary);
        Assert.Equal(h.BranchA, after.LocationId);
        Assert.True(after.IsActive);
    }
}
