using System.Reflection;
using System.Security.Claims;
using System.Threading.Channels;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Unclosed days are now a branch manager's job too, and this is the line that job stops at.
///
/// A day with a check-in and no check-out scores ZERO hours — the person worked and their month says
/// they did not. Only the branch knows which of those is a forgotten scan and which is somebody who
/// went home early, so leaving the whole screen with the admin meant a manager watching their own
/// people's time disappear with nothing they could do about it. Hence the widening.
///
/// What must not travel with it is reach over accounts that are not theirs. A manager may act only on
/// a Role==Employee target inside a branch they oversee — the 2026-08-08 rule that stopped a manager
/// reaching a same-branch ADMIN, who carries a LocationId like everybody else because they clock in
/// too. Closing an admin's day is not a takeover on its own, but it writes a pay-critical time onto an
/// account above the manager's, and the point of that rule is that branch membership alone never
/// grants that.
///
/// The reflection tests at the bottom guard the other half. The class attribute now admits Managers,
/// so the two actions that must stay with the admin depend entirely on their own attributes — and an
/// attribute is exactly the kind of thing dropped in a later edit with nothing else noticing.
/// </summary>
public class OpenRecordsManagerScopeTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-0000000000e8");

    /// <summary>The UTC day — the same one Open() compares AttendanceDate against.</summary>
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateTime At(DateOnly date, int hour) =>
        DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, 0)), DateTimeKind.Utc);

    private sealed class Fixture : IDisposable
    {
        public AppDbContext Db { get; }

        public Guid ManagedBranch { get; } = Guid.NewGuid();
        public Guid OtherBranch { get; } = Guid.NewGuid();

        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public Guid MyWorkerId { get; } = Guid.NewGuid();
        public Guid TheirWorkerId { get; } = Guid.NewGuid();

        /// <summary>An admin who clocks in at the branch our manager oversees — the P0 row.</summary>
        public Guid SameBranchAdminId { get; } = Guid.NewGuid();

        // Unclosed days (check-in, no check-out) on a PAST date — what the screen lists.
        public Guid OpenMineId { get; } = Guid.NewGuid();
        public Guid OpenTheirsId { get; } = Guid.NewGuid();
        public Guid OpenAdminId { get; } = Guid.NewGuid();

        /// <summary>Open, but dated today: somebody still at work, not a forgotten scan.</summary>
        public Guid OpenTodayMineId { get; } = Guid.NewGuid();

        // Closed days, for the undo-a-check-out action.
        public Guid ClosedMineId { get; } = Guid.NewGuid();
        public Guid ClosedTheirsId { get; } = Guid.NewGuid();
        public Guid ClosedAdminId { get; } = Guid.NewGuid();

        public static readonly DateOnly OpenDate = Today.AddDays(-3);
        public static readonly DateOnly ClosedDate = Today.AddDays(-5);

        public Fixture()
        {
            var tenant = new TenantContext();
            tenant.Resolve(TenantId);
            Db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase($"open-records-{Guid.NewGuid()}").Options,
                tenant);

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId, Name = "T", Slug = "t", DisplayName = "T", IsActive = true,
            });
            Db.Locations.Add(Branch(ManagedBranch, "Menim filialim"));
            Db.Locations.Add(Branch(OtherBranch, "Basqa filial"));

            // Scope comes from ManagedLocations, never from the manager's own LocationId.
            Db.ManagedLocations.Add(new ManagedLocation
            {
                EmployeeId = ManagerId, LocationId = ManagedBranch, TenantId = TenantId,
            });

            Db.Employees.Add(Person(ManagerId, "Menecer", EmployeeRole.Manager, ManagedBranch));
            Db.Employees.Add(Person(AdminId, "Sirket Admini", EmployeeRole.Admin, ManagedBranch));
            Db.Employees.Add(Person(MyWorkerId, "Menim Iscim", EmployeeRole.Employee, ManagedBranch));
            Db.Employees.Add(Person(TheirWorkerId, "Ozge Isci", EmployeeRole.Employee, OtherBranch));
            Db.Employees.Add(Person(SameBranchAdminId, "Eyni Filialda Admin", EmployeeRole.Admin, ManagedBranch));

            Db.AttendanceRecords.Add(Record(OpenMineId, MyWorkerId, ManagedBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenTheirsId, TheirWorkerId, OtherBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenAdminId, SameBranchAdminId, ManagedBranch, OpenDate, closed: false));
            Db.AttendanceRecords.Add(Record(OpenTodayMineId, MyWorkerId, ManagedBranch, Today, closed: false));
            Db.AttendanceRecords.Add(Record(ClosedMineId, MyWorkerId, ManagedBranch, ClosedDate, closed: true));
            Db.AttendanceRecords.Add(Record(ClosedTheirsId, TheirWorkerId, OtherBranch, ClosedDate, closed: true));
            Db.AttendanceRecords.Add(Record(ClosedAdminId, SameBranchAdminId, ManagedBranch, ClosedDate, closed: true));

            Db.SaveChanges();
        }

        private static Location Branch(Guid id, string name) => new()
        {
            Id = id, TenantId = TenantId, Name = name,
            Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0),
            LateThresholdMinutes = 15, QrVersion = 1, IsActive = true,
        };

        private static Employee Person(Guid id, string name, EmployeeRole role, Guid locationId) => new()
        {
            Id = id, TenantId = TenantId, FullName = name, Role = role, LocationId = locationId,
            IsActive = true, PasswordHash = "h", ActivatedAtUtc = DateTime.UtcNow,
        };

        private static AttendanceRecord Record(
            Guid id, Guid employeeId, Guid locationId, DateOnly date, bool closed) => new()
        {
            Id = id, TenantId = TenantId, EmployeeId = employeeId, LocationId = locationId,
            AttendanceDate = date, CheckInAtUtc = At(date, 9),
            CheckOutAtUtc = closed ? At(date, 18) : null,
            Status = closed ? AttendanceStatus.OnTime : AttendanceStatus.Incomplete,
        };

        public AdminAttendanceController As(Guid who, EmployeeRole role) =>
            new(Db, new StubSummary(), new StubQueue(), new AppOptions { TimeZone = "Asia/Baku" })
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("sub", who.ToString()),
                            new Claim("role", role.ToString()),
                        }, "test")),
                    },
                },
            };

        public AdminAttendanceController AsManager() => As(ManagerId, EmployeeRole.Manager);

        public AdminAttendanceController AsAdmin() => As(AdminId, EmployeeRole.Admin);

        /// <summary>The stored row, read past the change tracker so a refused write cannot look applied.</summary>
        public AttendanceRecord Row(Guid recordId) =>
            Db.AttendanceRecords.AsNoTracking().Single(r => r.Id == recordId);

        public int AuditRows() => Db.AuditLogs.AsNoTracking().Count();

        public void Dispose() => Db.Dispose();
    }

    private sealed class StubSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubQueue : IFaceMatchQueue
    {
        public void Enqueue(Guid tenantId, Guid recordId) { }
        public ChannelReader<FaceMatchJob> Reader => Channel.CreateUnbounded<FaceMatchJob>().Reader;
    }

    private static List<Guid> ListedRecordIds(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<object>()
            .Select(r => (Guid)r.GetType().GetProperty("recordId")!.GetValue(r)!)
            .ToList();
    }

    private static void AssertOutOfScope(IActionResult result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        // The screen switches on this code to explain the refusal instead of showing a bare failure.
        Assert.Equal("OutOfScope", obj.Value!.GetType().GetProperty("error")!.GetValue(obj.Value));
    }

    /// <summary>Closing a day the way the screen does: fill in the missing departure time.</summary>
    private static AdminAttendanceUpdateRequest CloseAt(DateOnly date) => new(null, At(date, 18));

    // --- the list ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_managers_list_is_their_own_branches_and_stops_there()
    {
        // The list is capped at 500 rows. A manager shown the whole company would not merely see too
        // much: a bigger branch's backlog would push their own people off the end of it, which is the
        // one thing this screen exists to prevent.
        using var f = new Fixture();

        var ids = ListedRecordIds(await f.AsManager().Open());

        Assert.Contains(f.OpenMineId, ids);
        Assert.DoesNotContain(f.OpenTheirsId, ids);
    }

    [Fact]
    public async Task An_admin_still_sees_the_whole_company()
    {
        // Narrowing the manager must not have narrowed the admin — they are the only one who can reach
        // a branch whose manager is unassigned, and three CleanFix branches still are.
        using var f = new Fixture();

        var ids = ListedRecordIds(await f.AsAdmin().Open());

        Assert.Contains(f.OpenMineId, ids);
        Assert.Contains(f.OpenTheirsId, ids);
        Assert.Contains(f.OpenAdminId, ids);
    }

    [Fact]
    public async Task Todays_open_day_is_offered_to_nobody()
    {
        // An open record dated today is somebody still at work. Listing it invites a manager to "close"
        // a shift that has not finished, stamping a departure time the employee never had.
        using var f = new Fixture();

        Assert.DoesNotContain(f.OpenTodayMineId, ListedRecordIds(await f.AsManager().Open()));
        Assert.DoesNotContain(f.OpenTodayMineId, ListedRecordIds(await f.AsAdmin().Open()));
    }

    [Fact]
    public async Task Seeing_a_row_is_not_being_able_to_close_it()
    {
        // The seam between the two rules, pinned in one place. The list is filtered by BRANCH, so a
        // same-branch admin's unclosed day is visible to their manager — the same information they get
        // from standing at the gate. Acting on it is the stricter question, and the answer is no.
        using var f = new Fixture();

        Assert.Contains(f.OpenAdminId, ListedRecordIds(await f.AsManager().Open()));
        AssertOutOfScope(await f.AsManager().Update(f.OpenAdminId, CloseAt(Fixture.OpenDate)));
    }

    // --- closing a day (PUT) ----------------------------------------------------------------------

    [Fact]
    public async Task A_manager_closes_their_own_staffs_day()
    {
        // The reason the screen was widened at all: this record scored zero hours until somebody who
        // knew what happened could put the departure time on it.
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsManager().Update(f.OpenMineId, CloseAt(Fixture.OpenDate)));

        Assert.Equal(At(Fixture.OpenDate, 18), f.Row(f.OpenMineId).CheckOutAtUtc);
        // Typed by hand, not scanned — a pay-critical time must never be indistinguishable from a scan.
        Assert.Equal(f.ManagerId, f.Row(f.OpenMineId).ManualByEmployeeId);
        Assert.Equal(1, f.AuditRows());
    }

    [Fact]
    public async Task A_manager_cannot_write_a_time_onto_a_same_branch_admins_day()
    {
        // The 2026-08-08 boundary, in the place it is easiest to lose: an Admin has a LocationId
        // because they clock in, so a branch-only check would make them a perfectly legal target.
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().Update(f.OpenAdminId, CloseAt(Fixture.OpenDate)));

        Assert.Null(f.Row(f.OpenAdminId).CheckOutAtUtc);
        Assert.Null(f.Row(f.OpenAdminId).ManualByEmployeeId);
        // A refusal is not an edit. An audit trail that records both stops being evidence of either.
        Assert.Equal(0, f.AuditRows());
    }

    [Fact]
    public async Task A_manager_cannot_write_a_time_onto_another_branchs_day()
    {
        // Update takes a record id straight from the URL, so the filter on the list protects nothing
        // here — the id of a record they never saw works just as well as one they did.
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().Update(f.OpenTheirsId, CloseAt(Fixture.OpenDate)));

        Assert.Null(f.Row(f.OpenTheirsId).CheckOutAtUtc);
        Assert.Equal(0, f.AuditRows());
    }

    // --- undoing a check-out ----------------------------------------------------------------------

    [Fact]
    public async Task A_manager_undoes_their_own_staffs_accidental_check_out()
    {
        // A double scan at the gate checks somebody out minutes after they arrived; without this their
        // whole day is five minutes long, and Update cannot fix it (a null check-out means "leave").
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsManager().ClearCheckOut(f.ClosedMineId));

        Assert.Null(f.Row(f.ClosedMineId).CheckOutAtUtc);
        Assert.Equal(f.ManagerId, f.Row(f.ClosedMineId).ManualByEmployeeId);
    }

    [Fact]
    public async Task A_manager_cannot_reopen_a_same_branch_admins_day()
    {
        // Same target, the other verb. Both writes have to ask the same question, or the boundary is
        // only as strong as whichever endpoint the next change remembers.
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().ClearCheckOut(f.ClosedAdminId));

        Assert.Equal(At(Fixture.ClosedDate, 18), f.Row(f.ClosedAdminId).CheckOutAtUtc);
        Assert.Equal(0, f.AuditRows());
    }

    [Fact]
    public async Task A_manager_cannot_reopen_another_branchs_day()
    {
        using var f = new Fixture();

        AssertOutOfScope(await f.AsManager().ClearCheckOut(f.ClosedTheirsId));

        Assert.Equal(At(Fixture.ClosedDate, 18), f.Row(f.ClosedTheirsId).CheckOutAtUtc);
    }

    [Fact]
    public async Task An_admin_passes_through_both_writes()
    {
        // The corrections an admin could always make. If scoping ever starts applying to them too, the
        // records nobody else can reach — another branch's, another admin's — become uncorrectable.
        using var f = new Fixture();

        Assert.IsType<OkObjectResult>(await f.AsAdmin().Update(f.OpenAdminId, CloseAt(Fixture.OpenDate)));
        Assert.IsType<OkObjectResult>(await f.AsAdmin().ClearCheckOut(f.ClosedTheirsId));

        Assert.Equal(At(Fixture.OpenDate, 18), f.Row(f.OpenAdminId).CheckOutAtUtc);
        Assert.Null(f.Row(f.ClosedTheirsId).CheckOutAtUtc);
    }

    // --- what the widening did NOT hand over ------------------------------------------------------

    [Theory]
    [InlineData("Create")]
    [InlineData("RecheckFaces")]
    public void The_wider_powers_keep_their_own_admin_only_attribute(string action)
    {
        // The class now says "Admin,Manager", so these two are protected by nothing except the
        // attribute on the method itself. Create writes a working day out of nothing for ANY employee
        // — there is no existing record to scope against, so the checks above have nothing to check —
        // and RecheckFaces spends a paid Rekognition call per record across the whole tenant. Drop
        // either attribute and both quietly become manager powers with no other test failing.
        var method = typeof(AdminAttendanceController).GetMethod(action, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Admin", attr!.Roles);
    }

    [Fact]
    public void The_screen_a_manager_gained_says_so_on_the_class()
    {
        var attr = typeof(AdminAttendanceController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("Admin,Manager", attr!.Roles);
    }
}
