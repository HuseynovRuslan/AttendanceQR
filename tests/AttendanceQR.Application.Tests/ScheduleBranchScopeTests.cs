using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A shift belongs to one branch, or to the whole company — and the difference has to hold in the
/// picker, on the server, and when a branch is deleted.
///
/// Shifts were company-wide with no alternative. With one branch that is invisible; the moment there
/// are ten it becomes a list of twenty shifts on every employee's card, and nothing stops "FM 2-ci
/// növbə (13:00–23:00)" — one site's afternoon cleaning crew — being put on somebody at another site.
/// That mistake shows up nowhere afterwards. It is not an error, not a flag, not a red row: it is
/// simply hours that quietly do not match the work, and a person marked late or absent by a schedule
/// they were never on.
///
/// Null still means shared, which is what every shift created before this was, so nothing about the
/// three live companies changes until somebody picks a branch.
/// </summary>
public class ScheduleBranchScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; }
        public Guid BranchA { get; } = Guid.NewGuid();
        public Guid BranchB { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid WorkerAtA { get; } = Guid.NewGuid();
        public Guid WorkerAtB { get; } = Guid.NewGuid();

        public Fixture()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"sched-branch-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant { Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true });
            Db.Locations.Add(Branch(BranchA, "Fəvvarələr Meydanı"));
            Db.Locations.Add(Branch(BranchB, "Nizami"));
            Db.Employees.Add(Person(AdminId, "Admin", EmployeeRole.Admin, BranchA));
            Db.Employees.Add(Person(WorkerAtA, "FM işçisi", EmployeeRole.Employee, BranchA));
            Db.Employees.Add(Person(WorkerAtB, "Nizami işçisi", EmployeeRole.Employee, BranchB));
            Db.SaveChanges();
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name, Latitude = 40.4, Longitude = 49.8,
            RadiusMeters = 150, ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid branch) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = branch,
            IsActive = true, ActivatedAtUtc = DateTime.UtcNow, PasswordHash = "h",
        };

        public Guid AddShift(string name, Guid? locationId)
        {
            var s = new Schedule
            {
                TenantId = TenantId, Name = name, LocationId = locationId,
                ShiftStart = new TimeOnly(13, 0), ShiftEnd = new TimeOnly(23, 0),
                LateThresholdMinutes = 15, WorkDaysMask = 126,
            };
            Db.Schedules.Add(s);
            Db.SaveChanges();
            return s.Id;
        }

        public SchedulesController Schedules(Guid who, EmployeeRole role) =>
            new(Db) { ControllerContext = Context(who, role) };

        private static ControllerContext Context(Guid who, EmployeeRole role) => new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("sub", who.ToString()),
                    new Claim("role", role.ToString()),
                ], "test")),
            },
        };

        public void Dispose() => Db.Dispose();
    }

    private static string ErrorOf(IActionResult result)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        return obj.Value?.GetType().GetProperty("error")?.GetValue(obj.Value)?.ToString() ?? "";
    }

    private static List<string> NamesOf(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!)
            .Cast<object>()
            .Select(o => o.GetType().GetProperty("name")!.GetValue(o)!.ToString()!)
            .ToList();
    }

    // --- creating ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_shift_with_no_branch_belongs_to_the_whole_company()
    {
        // The old behaviour, which has to stay the default: every shift that exists today is this.
        using var f = new Fixture();
        var result = await f.Schedules(f.AdminId, EmployeeRole.Admin)
            .Create(new ScheduleRequest("Gündüz", "09:00", "18:00"));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Null(ok.Value!.GetType().GetProperty("locationId")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task A_shift_can_be_pinned_to_a_branch()
    {
        using var f = new Fixture();
        var result = await f.Schedules(f.AdminId, EmployeeRole.Admin)
            .Create(new ScheduleRequest("FM 2-ci növbə", "13:00", "23:00", LocationId: f.BranchA));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(f.BranchA, ok.Value!.GetType().GetProperty("locationId")!.GetValue(ok.Value));
    }

    [Fact]
    public async Task A_branch_that_does_not_exist_is_refused()
    {
        // The query filter is tenant-scoped, so another company's branch id reads as missing — which
        // is the point: a shift must never straddle two companies.
        using var f = new Fixture();
        var result = await f.Schedules(f.AdminId, EmployeeRole.Admin)
            .Create(new ScheduleRequest("Səhv", "09:00", "18:00", LocationId: Guid.NewGuid()));

        Assert.Equal("LocationNotFound", ErrorOf(result));
    }

    // --- assigning --------------------------------------------------------------------------------

    [Fact]
    public async Task A_branch_shift_cannot_be_given_to_somebody_at_another_branch()
    {
        // The mistake this whole feature exists to prevent, at the one place it can still be made.
        using var f = new Fixture();
        var shift = f.AddShift("FM 2-ci növbə", f.BranchA);

        var controller = new AdminController_ScheduleProbe(f.Db);
        var error = await controller.TryAssign(f.WorkerAtB, shift);

        Assert.Equal("ScheduleBelongsToOtherBranch", error);
    }

    [Fact]
    public async Task A_branch_shift_is_fine_for_that_branchs_own_staff()
    {
        using var f = new Fixture();
        var shift = f.AddShift("FM 2-ci növbə", f.BranchA);

        var error = await new AdminController_ScheduleProbe(f.Db).TryAssign(f.WorkerAtA, shift);

        Assert.Null(error);
    }

    [Fact]
    public async Task A_company_wide_shift_is_fine_for_anybody()
    {
        using var f = new Fixture();
        var shift = f.AddShift("Gündüz", null);
        var probe = new AdminController_ScheduleProbe(f.Db);

        Assert.Null(await probe.TryAssign(f.WorkerAtA, shift));
        Assert.Null(await probe.TryAssign(f.WorkerAtB, shift));
    }

    // --- who sees what ----------------------------------------------------------------------------

    [Fact]
    public async Task An_admin_sees_every_shift_in_the_company()
    {
        using var f = new Fixture();
        f.AddShift("Gündüz", null);
        f.AddShift("FM 2-ci növbə", f.BranchA);
        f.AddShift("Nizami gecə", f.BranchB);

        var names = NamesOf(await f.Schedules(f.AdminId, EmployeeRole.Admin).List());
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public async Task A_manager_sees_the_shared_shifts_and_their_own_branches()
    {
        using var f = new Fixture();
        var managerId = Guid.NewGuid();
        f.Db.Employees.Add(new Employee
        {
            Id = managerId, TenantId = TenantId, FullName = "Menecer", Role = EmployeeRole.Manager,
            LocationId = f.BranchA, IsActive = true, PasswordHash = "h",
        });
        f.Db.ManagedLocations.Add(new ManagedLocation { EmployeeId = managerId, LocationId = f.BranchA });
        f.Db.SaveChanges();

        f.AddShift("Gündüz", null);
        f.AddShift("FM 2-ci növbə", f.BranchA);
        f.AddShift("Nizami gecə", f.BranchB);

        var names = NamesOf(await f.Schedules(managerId, EmployeeRole.Manager).List());

        Assert.Contains("Gündüz", names);          // shared
        Assert.Contains("FM 2-ci növbə", names);   // theirs
        Assert.DoesNotContain("Nizami gecə", names); // somebody else's crew
    }

    [Fact]
    public async Task The_shared_shifts_are_listed_first()
    {
        // They are the ones that apply to whoever is reading, so they belong at the top of a picker
        // rather than interleaved by creation date with nine other branches' shifts.
        using var f = new Fixture();
        f.AddShift("FM 2-ci növbə", f.BranchA);
        f.AddShift("Gündüz", null);

        Assert.Equal("Gündüz", NamesOf(await f.Schedules(f.AdminId, EmployeeRole.Admin).List())[0]);
    }

    // --- moving one -------------------------------------------------------------------------------

    [Fact]
    public async Task Pinning_a_shift_that_other_branches_are_already_on_is_refused()
    {
        // Otherwise the people left behind keep a ScheduleId their branch no longer offers: their
        // hours would come from a shift nobody can see on their card, and no screen would say so.
        using var f = new Fixture();
        var shift = f.AddShift("Gündüz", null);
        var worker = f.Db.Employees.First(e => e.Id == f.WorkerAtB);
        worker.ScheduleId = shift;
        f.Db.SaveChanges();

        var result = await f.Schedules(f.AdminId, EmployeeRole.Admin)
            .Update(shift, new ScheduleRequest("Gündüz", "09:00", "18:00", LocationId: f.BranchA));

        Assert.Equal("ScheduleUsedByOtherBranch", ErrorOf(result));
        Assert.Null(f.Db.Schedules.AsNoTracking().First(s => s.Id == shift).LocationId);
    }

    [Fact]
    public async Task Pinning_is_allowed_when_everybody_on_it_is_already_at_that_branch()
    {
        using var f = new Fixture();
        var shift = f.AddShift("Gündüz", null);
        var worker = f.Db.Employees.First(e => e.Id == f.WorkerAtA);
        worker.ScheduleId = shift;
        f.Db.SaveChanges();

        var result = await f.Schedules(f.AdminId, EmployeeRole.Admin)
            .Update(shift, new ScheduleRequest("FM Gündüz", "09:00", "18:00", LocationId: f.BranchA));

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(f.BranchA, f.Db.Schedules.AsNoTracking().First(s => s.Id == shift).LocationId);
    }

    /// <summary>
    /// The REAL rule both controllers call — not a copy of it. AdminController takes a dozen services
    /// this test has no use for, so the decision lives in ScheduleAssignmentRule where it can be
    /// reached directly; that is the same move TemporaryPinGate made, and for the same reason.
    /// </summary>
    private sealed class AdminController_ScheduleProbe(AppDbContext db)
    {
        public async Task<string?> TryAssign(Guid employeeId, Guid scheduleId)
        {
            var employee = await db.Employees.FirstAsync(e => e.Id == employeeId);
            var schedule = await db.Schedules.Where(s => s.Id == scheduleId)
                .Select(s => new { s.LocationId }).FirstOrDefaultAsync();
            if (schedule is null) return "ScheduleNotFound";
            return AttendanceQR.Api.ScheduleAssignmentRule.Refusal(schedule.LocationId, employee.LocationId);
        }
    }
}
