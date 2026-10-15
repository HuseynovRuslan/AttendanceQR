using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Structures — the optional layer between a company and its branches (docs/strukturlar-ve-selahiyyetler.md,
/// Phase 1). These pin what a structure must never do: change anything for a company that has none, reach
/// into another company, or take a branch's history with it when the branch moves.
/// </summary>
public class StructureTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-00000000a5a1");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-00000000a5b2");

    private sealed class World : IDisposable
    {
        private readonly string _db = $"structures-{Guid.NewGuid()}";
        private readonly List<AppDbContext> _open = [];

        public Guid AdminA { get; } = Guid.NewGuid();
        public Guid Park { get; } = Guid.NewGuid();
        public Guid Road { get; } = Guid.NewGuid();
        public Guid Square { get; } = Guid.NewGuid();
        public Guid BranchOfB { get; } = Guid.NewGuid();
        public Guid Worker { get; } = Guid.NewGuid();

        public World()
        {
            using var a = Context(TenantA);
            a.Tenants.Add(new Tenant { Id = TenantA, Name = "A", Slug = "a", DisplayName = "A", IsActive = true });
            a.Tenants.Add(new Tenant { Id = TenantB, Name = "B", Slug = "b", DisplayName = "B", IsActive = true });
            foreach (var (id, name) in new[] { (Park, "Mərkəzi park"), (Road, "Aeroport yolu 1"), (Square, "Fəvvarələr") })
                a.Locations.Add(new Location { Id = id, TenantId = TenantA, Name = name, IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
            a.Locations.Add(new Location { Id = BranchOfB, TenantId = TenantB, Name = "B filialı", IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
            a.Employees.Add(new Employee
            {
                Id = AdminA, TenantId = TenantA, FullName = "Admin", Email = "admin@a.local", Role = EmployeeRole.Admin,
                LocationId = Park, IsActive = true, PasswordHash = "x",
            });
            a.Employees.Add(new Employee
            {
                Id = Worker, TenantId = TenantA, FullName = "İşçi", Email = "isci@a.local", Role = EmployeeRole.Employee,
                LocationId = Park, IsActive = true, PasswordHash = "x",
            });
            a.SaveChanges();
        }

        public AppDbContext Context(Guid tenantId)
        {
            var tenant = new TenantContext();
            tenant.Resolve(tenantId);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_db).Options, tenant);
            _open.Add(db);
            return db;
        }

        public StructuresController Admin(Guid tenantId) => new(Context(tenantId))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", AdminA.ToString()), new Claim("role", "Admin")], "test")),
                },
            },
        };

        public async Task<Guid> CreateAsync(string name, Guid? tenantId = null)
        {
            var ok = Assert.IsType<OkObjectResult>(await Admin(tenantId ?? TenantA).Create(new StructureRequest(name)));
            return (Guid)ok.Value!.GetType().GetProperty("id")!.GetValue(ok.Value)!;
        }

        public Guid? StructureOf(Guid locationId) =>
            Context(TenantA).Locations.IgnoreQueryFilters().AsNoTracking().Single(l => l.Id == locationId).StructureId;

        public void Dispose()
        {
            foreach (var db in _open)
                db.Dispose();
        }
    }

    private static T Prop<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static string ErrorOf(IActionResult result) =>
        Prop<string>(((ObjectResult)result).Value!, "error");

    // --- a company with no structures ------------------------------------------------------------------

    [Fact]
    public async Task A_company_without_structures_sees_every_branch_as_it_was()
    {
        using var w = new World();

        var ok = Assert.IsType<OkObjectResult>(await w.Admin(TenantA).List());

        Assert.Empty(Prop<IEnumerable<object>>(ok.Value!, "structures"));
        var unassigned = Prop<IEnumerable<object>>(ok.Value!, "unassigned").Select(b => Prop<string>(b, "name")).ToList();
        Assert.Equal(["Aeroport yolu 1", "Fəvvarələr", "Mərkəzi park"], unassigned);
    }

    // --- create, rename, archive ------------------------------------------------------------------------

    [Theory]
    [InlineData("", "NameRequired")]
    [InlineData("   ", "NameRequired")]
    public async Task A_structure_needs_a_name(string name, string error)
    {
        using var w = new World();

        var refused = await w.Admin(TenantA).Create(new StructureRequest(name));

        Assert.IsType<BadRequestObjectResult>(refused);
        Assert.Equal(error, ErrorOf(refused));
    }

    [Fact]
    public async Task A_name_is_taken_once_per_company_whatever_its_case_but_an_archived_one_frees_it()
    {
        using var w = new World();
        var first = await w.CreateAsync("Aeroport yolu");

        var again = await w.Admin(TenantA).Create(new StructureRequest("aeroport YOLU"));
        Assert.IsType<ConflictObjectResult>(again);
        Assert.Equal("NameTaken", ErrorOf(again));

        Assert.IsType<OkObjectResult>(await w.Admin(TenantA).Archive(first));
        Assert.IsType<OkObjectResult>(await w.Admin(TenantA).Create(new StructureRequest("Aeroport yolu")));
        // …and the archived one cannot come back while the name is in use.
        Assert.Equal("NameTaken", ErrorOf(await w.Admin(TenantA).Restore(first)));
    }

    [Fact]
    public async Task Another_company_may_use_the_same_name()
    {
        using var w = new World();
        await w.CreateAsync("Parklar");

        Assert.IsType<OkObjectResult>(await w.Admin(TenantB).Create(new StructureRequest("Parklar")));
    }

    [Fact]
    public async Task A_structure_with_branches_is_not_archived()
    {
        using var w = new World();
        var roads = await w.CreateAsync("Yollar");
        await w.Admin(TenantA).Assign(new AssignStructureRequest(roads, [w.Road]));

        var refused = await w.Admin(TenantA).Archive(roads);

        Assert.IsType<ConflictObjectResult>(refused);
        Assert.Equal("StructureNotEmpty", ErrorOf(refused));
        Assert.Equal(roads, w.StructureOf(w.Road));
    }

    // --- moving branches -------------------------------------------------------------------------------

    [Fact]
    public async Task Branches_move_between_structures_and_out_of_them_with_every_move_on_the_record()
    {
        using var w = new World();
        var parks = await w.CreateAsync("Parklar");
        var roads = await w.CreateAsync("Yollar");

        await w.Admin(TenantA).Assign(new AssignStructureRequest(parks, [w.Park, w.Road]));
        await w.Admin(TenantA).Assign(new AssignStructureRequest(roads, [w.Road]));
        await w.Admin(TenantA).Assign(new AssignStructureRequest(null, [w.Park]));

        Assert.Null(w.StructureOf(w.Park));
        Assert.Equal(roads, w.StructureOf(w.Road));
        var moves = w.Context(TenantA).AuditLogs.AsNoTracking()
            .Where(a => a.EventType == AuditEventType.LocationStructureChanged)
            .OrderBy(a => a.CreatedAtUtc).Select(a => a.Reason).ToList();
        Assert.Contains("Aeroport yolu 1: Parklar → Yollar", moves);
        Assert.Contains("Mərkəzi park: Parklar → struktursuz", moves);
        Assert.Equal(4, moves.Count);
    }

    [Fact]
    public async Task An_archived_structure_takes_no_branches()
    {
        using var w = new World();
        var old = await w.CreateAsync("Köhnə");
        await w.Admin(TenantA).Archive(old);

        var refused = await w.Admin(TenantA).Assign(new AssignStructureRequest(old, [w.Park]));

        Assert.Equal("StructureArchived", ErrorOf(refused));
        Assert.Null(w.StructureOf(w.Park));
    }

    [Fact]
    public async Task A_branch_keeps_its_history_when_it_moves()
    {
        using var w = new World();
        var parks = await w.CreateAsync("Parklar");
        using (var db = w.Context(TenantA))
        {
            for (var day = 1; day <= 5; day++)
            {
                var date = new DateOnly(2026, 10, day);
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    TenantId = TenantA, EmployeeId = w.Worker, LocationId = w.Park, AttendanceDate = date,
                    CheckInAtUtc = date.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc),
                    CheckOutAtUtc = date.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc),
                });
                db.DailySummaries.Add(new DailySummary
                {
                    TenantId = TenantA, EmployeeId = w.Worker, LocationId = w.Park, SummaryDate = date, WorkedMinutes = 540,
                });
            }
            db.SaveChanges();
        }

        await w.Admin(TenantA).Assign(new AssignStructureRequest(parks, [w.Park]));
        await w.Admin(TenantA).Assign(new AssignStructureRequest(null, [w.Park]));

        var after = w.Context(TenantA);
        Assert.Equal(5, after.AttendanceRecords.Count(r => r.LocationId == w.Park && r.EmployeeId == w.Worker));
        Assert.Equal(5 * 540, after.DailySummaries.Where(s => s.LocationId == w.Park).Sum(s => s.WorkedMinutes));
    }

    // --- another company ---------------------------------------------------------------------------------

    [Fact]
    public async Task Another_companys_structure_is_not_found_and_nothing_moves()
    {
        using var w = new World();
        var theirs = await w.CreateAsync("Onların strukturu", TenantB);

        var refused = await w.Admin(TenantA).Assign(new AssignStructureRequest(theirs, [w.Park]));

        Assert.IsType<NotFoundObjectResult>(refused);
        Assert.Equal("StructureNotFound", ErrorOf(refused));
        Assert.Null(w.StructureOf(w.Park));
        Assert.IsType<NotFoundObjectResult>(await w.Admin(TenantA).Rename(theirs, new StructureRequest("Bizimki")));
        Assert.IsType<NotFoundObjectResult>(await w.Admin(TenantA).Archive(theirs));
    }

    [Fact]
    public async Task Another_companys_branch_cannot_be_pulled_into_a_structure_and_the_rest_do_not_move_either()
    {
        using var w = new World();
        var ours = await w.CreateAsync("Bizim struktur");

        var refused = await w.Admin(TenantA).Assign(new AssignStructureRequest(ours, [w.Park, w.BranchOfB]));

        Assert.Equal("LocationNotFound", ErrorOf(refused));
        Assert.Null(w.StructureOf(w.Park));
        Assert.Null(w.StructureOf(w.BranchOfB));
    }

    [Fact]
    public async Task One_company_never_sees_anothers_structures()
    {
        using var w = new World();
        await w.CreateAsync("Onların strukturu", TenantB);

        var ok = Assert.IsType<OkObjectResult>(await w.Admin(TenantA).List());

        Assert.Empty(Prop<IEnumerable<object>>(ok.Value!, "structures"));
        Assert.DoesNotContain("B filialı", Prop<IEnumerable<object>>(ok.Value!, "unassigned").Select(b => Prop<string>(b, "name")));
    }

    // --- who --------------------------------------------------------------------------------------------

    [Fact]
    public void Only_the_company_admin_manages_structures_until_permissions_land()
    {
        var attribute = typeof(StructuresController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal("Admin", attribute?.Roles);
    }
}
