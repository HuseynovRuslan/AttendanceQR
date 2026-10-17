using System.Security.Claims;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Application.Common;
using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Switching a person off — or back on — is «employees.deactivate», over the branch they work at. Found by the
/// staging E2E suite on 2026-10-17: the flag rode in on the manager desk's profile edit, which only asked for
/// «employees.update», so a Tabelçi (who holds that and not this) could lock anybody at their branch out. These
/// pin the rule on both endpoints that write the flag, in both modes, with the branch and company walls intact.
/// </summary>
public class DeactivatePermissionTests
{
    private sealed class NoSink : IAuthzShadowSink
    {
        public Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct) => Task.CompletedTask;
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
    private static AppDbContext Request(AccessWorld w, string permission)
    {
        var observer = new AuthzObserver(new NoSink(), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthzObserver>.Instance, new OperatorAllowlist(new HashSet<Guid> { w.Operator }));
        observer.AddRequestPermission(permission);
        return w.Context(AccessWorld.TenantA, observer);
    }

    private static async Task GiveAsync(AccessWorld w, Guid employee, string template, Guid branch)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var role = db.AccessRoles.Single(r => r.TemplateKey == template);
        db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = role.Id, Scope = AccessScopeType.Branch,
            LocationId = branch, CreatedByEmployeeId = w.Admin1,
        });
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

    private static AdminController AdminDesk(AccessWorld w, AppDbContext db, Guid caller) =>
        new(db, Options.Create(new InvitationOptions()), new Hasher(),
            new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())), w.Options, new NoPhotos(),
            NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", "Admin")], "test")),
                },
            },
        };

    private static ManagerEmployeeRequest Edit(Guid locationId, bool isActive, string fullName = "Dəyişməyən Ad") => new(
        FullName: fullName, Email: null, PhoneNumber: null, FatherName: null, Position: null,
        LocationId: locationId, IsActive: isActive);

    private static (bool Active, int TokenVersion) StateOf(AccessWorld w, Guid id)
    {
        var e = w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(x => x.Id == id);
        return (e.IsActive, e.TokenVersion);
    }

    private static string? ErrorOf(IActionResult result) =>
        (result as ObjectResult)?.Value?.GetType().GetProperty("error")?.GetValue((result as ObjectResult)!.Value) as string;

    private static async Task SetActiveAsync(AccessWorld w, Guid id, bool active)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Employees.Single(e => e.Id == id).IsActive = active;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_timekeeper_cannot_switch_a_person_off_or_back_on()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2 (home B2) is made the Tabelçi of B1, where E1 works.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);
        var before = StateOf(w, w.E1);

        var off = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2).UpdateEmployee(w.E1, Edit(w.B1, isActive: false));
        Assert.Equal(StatusCodes.Status403Forbidden, (off as ObjectResult)?.StatusCode);
        Assert.Equal("DeactivateNotAllowed", ErrorOf(off));
        // Nothing written, and nobody logged out.
        Assert.Equal(before, StateOf(w, w.E1));

        // Back on is the same right.
        await SetActiveAsync(w, w.E1, false);
        var on = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2).UpdateEmployee(w.E1, Edit(w.B1, isActive: true));
        Assert.Equal("DeactivateNotAllowed", ErrorOf(on));
        Assert.False(StateOf(w, w.E1).Active);
    }

    [Fact]
    public async Task A_timekeeper_still_edits_everything_else()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.E1, Edit(w.B1, isActive: true, fullName: "Yeni Ad"));
        Assert.IsType<OkObjectResult>(result);
        var e1 = w.Context(AccessWorld.TenantA).Employees.AsNoTracking().Single(x => x.Id == w.E1);
        Assert.Equal("Yeni Ad", e1.FullName);
        Assert.True(e1.IsActive);
    }

    [Fact]
    public async Task A_structure_operator_holding_the_right_switches_people_off_at_their_branch_only()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);
        var before = StateOf(w, w.E1);

        var off = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2).UpdateEmployee(w.E1, Edit(w.B1, isActive: false));
        Assert.IsType<OkObjectResult>(off);
        Assert.False(StateOf(w, w.E1).Active);
        // Their sessions end with it.
        Assert.Equal(before.TokenVersion + 1, StateOf(w, w.E1).TokenVersion);

        // Another branch's person is out of reach altogether — not even found.
        var elsewhere = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2).UpdateEmployee(w.E3, Edit(w.B3, isActive: false));
        Assert.IsType<NotFoundObjectResult>(elsewhere);
        Assert.True(StateOf(w, w.E3).Active);
    }

    [Fact]
    public async Task Todays_managers_keep_switching_their_own_staff_off_in_every_mode()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Shadow, AuthzMode.Enforced })
        {
            await SetModeAsync(w, mode);
            await SetActiveAsync(w, w.E1, true);
            // M1 manages B1 and B2.
            var off = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.M1).UpdateEmployee(w.E1, Edit(w.B1, isActive: false));
            Assert.True(off is OkObjectResult, $"{mode}: {ErrorOf(off)}");
            Assert.False(StateOf(w, w.E1).Active);
        }
    }

    [Fact]
    public async Task Another_companys_person_is_never_reached()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var result = await Desk(w, Request(w, AccessPermissions.EmployeesUpdate), w.E2)
            .UpdateEmployee(w.OtherCompanyManager, Edit(w.OtherBranch, isActive: false));
        Assert.IsType<NotFoundObjectResult>(result);
        var other = w.Context(AccessWorld.TenantB).Employees.AsNoTracking().Single(e => e.Id == w.OtherCompanyManager);
        Assert.True(other.IsActive);
    }

    [Fact]
    public async Task The_card_says_who_may_switch_the_person_off()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await GiveAsync(w, w.E4, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        static bool Deactivatable(IActionResult r)
        {
            var value = Assert.IsType<OkObjectResult>(r).Value!;
            return (bool)value.GetType().GetProperty("deactivatable")!.GetValue(value)!;
        }

        Assert.False(Deactivatable(await Desk(w, Request(w, AccessPermissions.EmployeesView), w.E2).Employee(w.E1)));
        Assert.True(Deactivatable(await Desk(w, Request(w, AccessPermissions.EmployeesView), w.E4).Employee(w.E1)));
    }

    [Fact]
    public async Task The_admin_desk_asks_the_same_right()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await SetModeAsync(w, AuthzMode.Enforced);
        using var db = Request(w, AccessPermissions.EmployeesUpdate);
        var e1 = db.Employees.AsNoTracking().Single(e => e.Id == w.E1);

        var result = await AdminDesk(w, db, w.Admin1).Update(w.E1, new EmployeeUpdateRequest(
            e1.FullName, e1.Email, e1.LocationId, e1.Role, IsActive: false, PhoneNumber: e1.PhoneNumber));
        // The Company administrator holds it company-wide: allowed, as before.
        Assert.IsType<OkObjectResult>(result);
        Assert.False(StateOf(w, w.E1).Active);
    }
}
