using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A person's check-in exceptions — «Səyyar» (CanFieldCheckIn), their own QR-free and GPS-wall settings and the
/// photo exemption — are rights of their own on the manager desk: «field_checkin.grant» and
/// «checkin_rules.override», not part of «employees.create» / «employees.update» (2026-11-11). The profile form
/// carries them on every save, so the rule is about what CHANGES: a value sent back as it is asks nothing, a
/// changed one asks for its right at the branch left and the branch joined. These pin the rule on both
/// endpoints, in every mode, with the branch and company walls intact.
/// </summary>
public class CheckInExceptionsPermissionTests
{
    private sealed class CountingSink : IAuthzShadowSink
    {
        public int Count;
        public Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class NoSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static async Task SetModeAsync(AccessWorld w, AuthzMode mode)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = mode;
        await db.SaveChangesAsync();
    }

    /// <summary>A request's context, as the pipeline hands it to the action: its own observer, holding the
    /// endpoint's permission (what AuthzEnforcedMiddleware adds).</summary>
    private static AppDbContext Request(AccessWorld w, string permission, IAuthzShadowSink? sink = null)
    {
        var observer = new AuthzObserver(sink ?? new CountingSink(), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthzObserver>.Instance, new OperatorAllowlist(new HashSet<Guid> { w.Operator }));
        observer.AddRequestPermission(permission);
        return w.Context(AccessWorld.TenantA, observer);
    }

    private static async Task GiveAsync(AccessWorld w, Guid employee, string template, params Guid[] branches)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var role = db.AccessRoles.Single(r => r.TemplateKey == template);
        Assign(db, w, employee, role.Id, branches);
        await db.SaveChangesAsync();
    }

    private static void Assign(AppDbContext db, AccessWorld w, Guid employee, Guid roleId, Guid[] branches)
    {
        foreach (var branch in branches)
            db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = roleId, Scope = AccessScopeType.Branch,
                LocationId = branch, CreatedByEmployeeId = w.Admin1,
            });
    }

    /// <summary>What the owner does in «Rollar»: the two optional rights added to the Struktur operatoru role
    /// (docs/strukturlar-ve-selahiyyetler.md §3.4 lists both as «istəyə bağlı»).</summary>
    private static async Task GiveOperatorTheExceptionRightsAsync(AccessWorld w)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var role = db.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.StructureOperator);
        role.Permissions = [.. role.Permissions, AccessPermissions.FieldCheckInGrant, AccessPermissions.CheckInRulesOverride];
        await db.SaveChangesAsync();
    }

    /// <summary>A company role holding only the two exception rights — given at fewer branches than the caller's
    /// edit right, to see that a move asks for them at both ends.</summary>
    private static async Task GiveExceptionsOnlyAsync(AccessWorld w, Guid employee, params Guid[] branches)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var role = new AccessRole
        {
            Id = Guid.NewGuid(), TenantId = AccessWorld.TenantA, Name = "Giriş istisnaları", CreatedAtUtc = DateTime.UtcNow,
            Permissions = [AccessPermissions.FieldCheckInGrant, AccessPermissions.CheckInRulesOverride],
        };
        db.AccessRoles.Add(role);
        Assign(db, w, employee, role.Id, branches);
        await db.SaveChangesAsync();
    }

    private static ManagerController Desk(AccessWorld w, AppDbContext db, Guid caller) =>
        new(db, new Hasher(), new NoSummary(), w.Options)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The role the middleware leaves on the request for a non-admin holder of the permission.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", "Manager")], "test")),
                },
            },
        };

    /// <summary>Everything the edit can write, as the database holds it — what a refusal must leave untouched.</summary>
    private sealed record State(
        string FullName, Guid LocationId, bool IsActive, int TokenVersion, string? PhoneNumber, string? Email,
        string? Position, bool CanFieldCheckIn, bool? QrlessCheckInOverride, bool? RequireGeofenceOverride, bool PhotoExempt);

    private static State StateOf(AccessWorld w, Guid id)
    {
        var e = w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(x => x.Id == id);
        return new State(e.FullName, e.LocationId, e.IsActive, e.TokenVersion, e.PhoneNumber, e.Email, e.Position,
            e.CanFieldCheckIn, e.QrlessCheckInOverride, e.RequireGeofenceOverride, e.PhotoExempt);
    }

    /// <summary>The form's body for an edit: the person's card sent back as it is (what the screen does).</summary>
    private static ManagerEmployeeRequest CardOf(AccessWorld w, Guid id)
    {
        var e = w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(x => x.Id == id);
        return new ManagerEmployeeRequest(
            FullName: e.FullName, Email: e.Email, PhoneNumber: e.PhoneNumber, FatherName: e.FatherName, Position: e.Position,
            LocationId: e.LocationId, PhotoExempt: e.PhotoExempt, CanFieldCheckIn: e.CanFieldCheckIn,
            QrlessCheckInOverride: e.QrlessCheckInOverride, RequireGeofenceOverride: e.RequireGeofenceOverride,
            IsActive: e.IsActive);
    }

    private static ManagerEmployeeRequest NewPerson(Guid branch, string phone) => new(
        FullName: "Yeni İşçi", Email: null, PhoneNumber: phone, FatherName: null, Position: null, LocationId: branch);

    /// <summary>Each exception, changed on its own — every one of them must ask.</summary>
    private static IEnumerable<(string Name, Func<ManagerEmployeeRequest, ManagerEmployeeRequest> Change, string Error)> Changes() =>
    [
        ("Səyyar", r => r with { CanFieldCheckIn = !r.CanFieldCheckIn }, "FieldCheckInNotAllowed"),
        ("QR-siz", r => r with { QrlessCheckInOverride = r.QrlessCheckInOverride is null ? true : null }, "CheckInRulesNotAllowed"),
        ("GPS divarı", r => r with { RequireGeofenceOverride = r.RequireGeofenceOverride is null ? false : null }, "CheckInRulesNotAllowed"),
        ("Foto", r => r with { PhotoExempt = !r.PhotoExempt }, "CheckInRulesNotAllowed"),
    ];

    private static string? ErrorOf(IActionResult result) =>
        (result as ObjectResult)?.Value?.GetType().GetProperty("error")?.GetValue((result as ObjectResult)!.Value) as string;

    private static int StatusOf(IActionResult result) => (result as ObjectResult)?.StatusCode ?? 0;

    private static int Headcount(AccessWorld w) => w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Count();

    private static async Task SetExceptionsAsync(AccessWorld w, Guid id)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var e = db.Employees.Single(x => x.Id == id);
        e.CanFieldCheckIn = true;
        e.QrlessCheckInOverride = true;
        e.RequireGeofenceOverride = false;
        e.PhotoExempt = true;
        await db.SaveChangesAsync();
    }

    // --- 1, 8: Enforced, a Tabelçi --------------------------------------------------------------------------

    [Fact]
    public async Task A_timekeeper_cannot_change_any_check_in_exception_and_nothing_is_written()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2 (home B2) is made the Tabelçi of B1, where E1 works.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        foreach (var (name, change, error) in Changes())
        {
            var before = StateOf(w, w.E1);
            // A name change rides along: the refusal is the whole request's, not just the exception's.
            var body = change(CardOf(w, w.E1)) with { FullName = "Başqa Ad" };

            var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2).UpdateEmployee(w.E1, body);

            Assert.True(StatusOf(result) == StatusCodes.Status403Forbidden, $"{name}: {StatusOf(result)} {ErrorOf(result)}");
            Assert.Equal(error, ErrorOf(result));
            Assert.Equal(before, StateOf(w, w.E1));
        }
    }

    [Fact]
    public async Task A_timekeeper_cannot_take_an_exception_away_either()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetExceptionsAsync(w, w.E1);
        await SetModeAsync(w, AuthzMode.Enforced);

        foreach (var (name, change, error) in Changes())
        {
            var before = StateOf(w, w.E1);
            var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
                .UpdateEmployee(w.E1, change(CardOf(w, w.E1)));
            Assert.True(ErrorOf(result) == error, $"{name}: {StatusOf(result)} {ErrorOf(result)}");
            Assert.Equal(before, StateOf(w, w.E1));
        }
    }

    // --- 2: the same values back are an ordinary edit -------------------------------------------------------

    [Fact]
    public async Task A_timekeeper_edits_everything_else_and_the_exceptions_stay_as_they_were()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetExceptionsAsync(w, w.E1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.E1, CardOf(w, w.E1) with { FullName = "Yeni Ad", Position = "Bağban" });

        Assert.True(result is OkObjectResult, ErrorOf(result));
        var after = StateOf(w, w.E1);
        Assert.Equal("Yeni Ad", after.FullName);
        Assert.Equal("Bağban", after.Position);
        Assert.True(after.CanFieldCheckIn);
        Assert.True(after.QrlessCheckInOverride);
        Assert.False(after.RequireGeofenceOverride);
        Assert.True(after.PhotoExempt);
    }

    // --- 3: holding the right is enough ---------------------------------------------------------------------

    [Fact]
    public async Task An_operator_holding_the_rights_changes_the_exceptions_at_their_branch()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveOperatorTheExceptionRightsAsync(w);
        await GiveAsync(w, w.E4, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E4)
            .UpdateEmployee(w.E1, CardOf(w, w.E1) with
            {
                CanFieldCheckIn = true, QrlessCheckInOverride = true, RequireGeofenceOverride = false, PhotoExempt = true,
            });

        Assert.True(result is OkObjectResult, ErrorOf(result));
        var after = StateOf(w, w.E1);
        Assert.True(after.CanFieldCheckIn);
        Assert.True(after.QrlessCheckInOverride);
        Assert.False(after.RequireGeofenceOverride);
        Assert.True(after.PhotoExempt);
    }

    [Fact]
    public async Task The_template_operator_without_the_optional_rights_is_refused_like_a_timekeeper()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E4, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);
        var before = StateOf(w, w.E1);

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E4)
            .UpdateEmployee(w.E1, CardOf(w, w.E1) with { RequireGeofenceOverride = false });

        Assert.Equal("CheckInRulesNotAllowed", ErrorOf(result));
        Assert.Equal(before, StateOf(w, w.E1));
    }

    // --- 4: the create ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_timekeeper_creates_with_the_safe_defaults_but_not_with_an_exception()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);
        var headcount = Headcount(w);

        var phone = 551000000;
        foreach (var (name, change, error) in Changes())
        {
            var result = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E2)
                .CreateEmployee(change(NewPerson(w.B1, (++phone).ToString())));
            Assert.True(ErrorOf(result) == error, $"{name}: {StatusOf(result)} {ErrorOf(result)}");
            Assert.Equal(headcount, Headcount(w));
        }

        // The defaults — no field check-in, the branch's rules, a photo taken — ask for nothing more.
        var plain = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E2)
            .CreateEmployee(NewPerson(w.B1, "559000001"));
        Assert.True(plain is OkObjectResult, ErrorOf(plain));
        Assert.Equal(headcount + 1, Headcount(w));
        var created = w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(e => e.PhoneNumber == "559000001");
        Assert.False(created.CanFieldCheckIn);
        Assert.Null(created.QrlessCheckInOverride);
        Assert.Null(created.RequireGeofenceOverride);
        Assert.False(created.PhotoExempt);
    }

    [Fact]
    public async Task An_operator_holding_the_rights_creates_a_person_with_their_exceptions()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveOperatorTheExceptionRightsAsync(w);
        await GiveAsync(w, w.E4, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E4)
            .CreateEmployee(NewPerson(w.B1, "559000002") with
            {
                CanFieldCheckIn = true, QrlessCheckInOverride = false, RequireGeofenceOverride = true, PhotoExempt = true,
            });

        Assert.True(result is OkObjectResult, ErrorOf(result));
        var created = w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(e => e.PhoneNumber == "559000002");
        Assert.True(created.CanFieldCheckIn);
        Assert.False(created.QrlessCheckInOverride);
        Assert.True(created.RequireGeofenceOverride);
        Assert.True(created.PhotoExempt);
    }

    // --- 5, 6: today's managers, in every mode ------------------------------------------------------------

    [Fact]
    public async Task Todays_managers_keep_setting_exceptions_on_their_own_staff_in_every_mode()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        var phone = 552000000;
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Shadow, AuthzMode.Enforced })
        {
            await SetModeAsync(w, mode);
            // M1 manages B1 and B2: switch everything on for E1, then move E2 from B2 to B1 with them on too.
            var on = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.M1).UpdateEmployee(w.E1, CardOf(w, w.E1) with
            {
                CanFieldCheckIn = true, QrlessCheckInOverride = true, RequireGeofenceOverride = false, PhotoExempt = true,
            });
            Assert.True(on is OkObjectResult, $"{mode}: {ErrorOf(on)}");
            Assert.True(StateOf(w, w.E1).CanFieldCheckIn);

            var moved = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.M1).UpdateEmployee(w.E2, CardOf(w, w.E2) with
            {
                LocationId = w.B1, CanFieldCheckIn = !StateOf(w, w.E2).CanFieldCheckIn,
            });
            Assert.True(moved is OkObjectResult, $"{mode} move: {ErrorOf(moved)}");

            // ...and back off, so the next mode starts from the same place.
            var off = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.M1).UpdateEmployee(w.E1, CardOf(w, w.E1) with
            {
                CanFieldCheckIn = false, QrlessCheckInOverride = null, RequireGeofenceOverride = null, PhotoExempt = false,
            });
            Assert.True(off is OkObjectResult, $"{mode} off: {ErrorOf(off)}");
            var back = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.M1)
                .UpdateEmployee(w.E2, CardOf(w, w.E2) with { LocationId = w.B2 });
            Assert.True(back is OkObjectResult, $"{mode} back: {ErrorOf(back)}");

            var created = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.M1)
                .CreateEmployee(NewPerson(w.B2, (++phone).ToString()) with { CanFieldCheckIn = true, PhotoExempt = true });
            Assert.True(created is OkObjectResult, $"{mode} create: {ErrorOf(created)}");
        }
    }

    [Fact]
    public async Task A_legacy_manager_still_edits_a_colleagues_exceptions_at_their_branch()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Shadow })
        {
            await SetModeAsync(w, mode);
            // M2 is M1's colleague at B2 (2026-09-18): today M1 edits their profile, exceptions included.
            var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.M1)
                .UpdateEmployee(w.M2, CardOf(w, w.M2) with { CanFieldCheckIn = !StateOf(w, w.M2).CanFieldCheckIn });
            Assert.True(result is OkObjectResult, $"{mode}: {ErrorOf(result)}");
        }
    }

    [Fact]
    public async Task In_shadow_the_new_check_records_no_disagreement_and_decides_nothing_new()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // A Tabelçi assignment exists — in Shadow it must not change what anybody may do.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Shadow);
        var sink = new CountingSink();

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate, sink), w.M1)
            .UpdateEmployee(w.E1, CardOf(w, w.E1) with { CanFieldCheckIn = true, RequireGeofenceOverride = false });

        Assert.True(result is OkObjectResult, ErrorOf(result));
        Assert.Equal(0, sink.Count);
        Assert.True(StateOf(w, w.E1).CanFieldCheckIn);
    }

    // --- 7: other branches, other companies ---------------------------------------------------------------

    [Fact]
    public async Task A_move_asks_for_the_right_at_the_branch_left_and_the_branch_joined()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2 edits people at B1 and B3 (Tabelçi at both), but holds the exception rights at B1 only.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1, w.B3);
        await GiveExceptionsOnlyAsync(w, w.E2, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        // At B1 the change is theirs to make.
        var stays = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.E1, CardOf(w, w.E1) with { QrlessCheckInOverride = true });
        Assert.True(stays is OkObjectResult, ErrorOf(stays));

        // Moving E1 to B3 with an exception changed: B3 is not covered — refused, nothing moved.
        var beforeOut = StateOf(w, w.E1);
        var outward = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.E1, CardOf(w, w.E1) with { LocationId = w.B3, CanFieldCheckIn = true });
        Assert.Equal("FieldCheckInNotAllowed", ErrorOf(outward));
        Assert.Equal(beforeOut, StateOf(w, w.E1));

        // Bringing E3 in from B3 with an exception changed: the branch they leave is not covered either.
        var beforeIn = StateOf(w, w.E3);
        var inward = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.E3, CardOf(w, w.E3) with { LocationId = w.B1, PhotoExempt = true });
        Assert.Equal("CheckInRulesNotAllowed", ErrorOf(inward));
        Assert.Equal(beforeIn, StateOf(w, w.E3));

        // The same move with the exceptions sent back unchanged is an ordinary move.
        var plainMove = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.E3, CardOf(w, w.E3) with { LocationId = w.B1 });
        Assert.True(plainMove is OkObjectResult, ErrorOf(plainMove));
        Assert.Equal(w.B1, StateOf(w, w.E3).LocationId);
    }

    [Fact]
    public async Task Another_branchs_person_is_out_of_reach_with_or_without_the_rights()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveOperatorTheExceptionRightsAsync(w);
        await GiveAsync(w, w.E4, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);
        var before = StateOf(w, w.E3);
        var headcount = Headcount(w);

        var edit = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E4)
            .UpdateEmployee(w.E3, CardOf(w, w.E3) with { CanFieldCheckIn = true, RequireGeofenceOverride = false });
        Assert.IsType<NotFoundObjectResult>(edit);
        Assert.Equal(before, StateOf(w, w.E3));

        var create = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E4)
            .CreateEmployee(NewPerson(w.B3, "559000003") with { CanFieldCheckIn = true });
        Assert.Equal("LocationNotManaged", ErrorOf(create));
        Assert.Equal(headcount, Headcount(w));
    }

    [Fact]
    public async Task Another_companys_person_and_branch_are_never_reached()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveOperatorTheExceptionRightsAsync(w);
        await GiveAsync(w, w.E4, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var edit = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E4).UpdateEmployee(w.OtherCompanyManager,
            new ManagerEmployeeRequest("Başqa şirkət", null, null, null, null, w.OtherBranch, CanFieldCheckIn: true));
        Assert.IsType<NotFoundObjectResult>(edit);
        var other = w.Context(AccessWorld.TenantB).Employees.AsNoTracking().Single(e => e.Id == w.OtherCompanyManager);
        Assert.False(other.CanFieldCheckIn);

        var create = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E4)
            .CreateEmployee(NewPerson(w.OtherBranch, "559000004") with { CanFieldCheckIn = true });
        Assert.Equal("LocationNotManaged", ErrorOf(create));
        Assert.False(w.Context(AccessWorld.TenantB).Employees.AsNoTracking().Any(e => e.PhoneNumber == "559000004"));
    }
}
