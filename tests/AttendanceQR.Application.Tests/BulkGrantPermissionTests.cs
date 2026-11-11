using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Api;
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
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The two bulk capabilities are two rights: «Ortaq telefon» is «devices.share_grant», «Sahə ziyarəti» is
/// «field_checkin.grant» (2026-11-11). Each has its own route on both desks, so the endpoint's gate names the right
/// one — a holder of the right gets in, a holder of the other does not — and inside, the capability's right is asked
/// again over each person's branch. These pin both halves, in every mode, with the branch and company walls intact.
/// </summary>
public class BulkGrantPermissionTests
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

    /// <summary>A company role carrying exactly these rights, given to one person at some branches.</summary>
    private static async Task GiveRoleAsync(AccessWorld w, Guid employee, string[] permissions, params Guid[] branches)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var role = new AccessRole
        {
            Id = Guid.NewGuid(), TenantId = AccessWorld.TenantA, Name = "Rol " + Guid.NewGuid().ToString("N")[..6],
            CreatedAtUtc = DateTime.UtcNow, Permissions = [.. permissions],
        };
        db.AccessRoles.Add(role);
        foreach (var branch in branches)
            db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = role.Id, Scope = AccessScopeType.Branch,
                LocationId = branch, CreatedByEmployeeId = w.Admin1,
            });
        await db.SaveChangesAsync();
    }

    private static async Task GiveTemplateAsync(AccessWorld w, Guid employee, string template, Guid branch)
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

    private static readonly string[] ShareOnly = [AccessPermissions.EmployeesView, AccessPermissions.DevicesShareGrant];
    private static readonly string[] FieldOnly = [AccessPermissions.EmployeesView, AccessPermissions.FieldCheckInGrant];
    private static readonly string[] Neither = [AccessPermissions.EmployeesView, AccessPermissions.EmployeesUpdate];
    private static readonly string[] Both =
        [AccessPermissions.EmployeesView, AccessPermissions.DevicesShareGrant, AccessPermissions.FieldCheckInGrant];

    private static ClaimsPrincipal As(Guid caller, string role) =>
        new(new ClaimsIdentity([new Claim("sub", caller.ToString()), new Claim("role", role)], "test"));

    private static ManagerController Desk(AppDbContext db, AccessWorld w, Guid caller) =>
        new(db, new Hasher(), new NoSummary(), w.Options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = As(caller, "Manager") } },
        };

    private static AdminController AdminDesk(AppDbContext db, AccessWorld w, Guid caller) =>
        new(db, Options.Create(new InvitationOptions()), new Hasher(),
            new MemoryCacheLoginLockoutStore(new MemoryCache(new MemoryCacheOptions())), w.Options, new NoPhotos(),
            NullLogger<AdminController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = As(caller, "Admin") } },
        };

    /// <summary>The Enforced gate of one endpoint for one person — what PermissionGateHandler decides before the action
    /// runs, read from the endpoint's own metadata.</summary>
    private static async Task<bool> GateAsync(AccessWorld w, Type controller, string action, Guid caller)
    {
        var method = controller.GetMethod(action)!;
        var access = (await AccessGraph.LoadAsync(w.Context(AccessWorld.TenantA))).For(caller);
        return PermissionGateHandler.Allows(
            AuthzShadowMiddleware.AuthorizeAttributesOf(method), AuthzShadowMiddleware.PermissionOf(method)!, access);
    }

    private static Task<IActionResult> GrantAsync(AccessWorld w, Guid caller, BulkPermission which, bool allowed, params Guid[] ids)
    {
        var request = new BulkGrantRequest(ids, allowed);
        var desk = Desk(Request(w, AdminController.RightFor(which)), w, caller);
        return which == BulkPermission.ShareDevice ? desk.BulkShareDevice(request) : desk.BulkFieldCheckIn(request);
    }

    private static (bool Share, bool Field) Flags(AccessWorld w, Guid id, Guid? tenant = null)
    {
        var e = w.Context(tenant ?? AccessWorld.TenantA).Employees.AsNoTracking().Single(x => x.Id == id);
        return (e.CanShareDevice, e.CanFieldCheckIn);
    }

    private static int Changed(IActionResult r) =>
        (int)Assert.IsType<OkObjectResult>(r).Value!.GetType().GetProperty("changed")!.GetValue(((OkObjectResult)r).Value)!;

    private static int Skipped(IActionResult r) =>
        (int)Assert.IsType<OkObjectResult>(r).Value!.GetType().GetProperty("skipped")!.GetValue(((OkObjectResult)r).Value)!;

    // --- the routes -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(AdminController))]
    [InlineData(typeof(ManagerController))]
    public void Each_capability_has_its_own_route_gated_by_its_own_right(Type controller)
    {
        var share = controller.GetMethod(nameof(ManagerController.BulkShareDevice))!;
        var field = controller.GetMethod(nameof(ManagerController.BulkFieldCheckIn))!;
        Assert.Equal(AccessPermissions.DevicesShareGrant, AuthzShadowMiddleware.PermissionOf(share)!.Key);
        Assert.Equal(AccessPermissions.FieldCheckInGrant, AuthzShadowMiddleware.PermissionOf(field)!.Key);
        Assert.EndsWith("bulk-permission/share-device", share.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.EndsWith("bulk-permission/field-check-in", field.GetCustomAttribute<HttpPostAttribute>()!.Template);

        // The single route that took the capability from the body — gated by one right for both — is gone.
        var templates = controller.GetMethods()
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template ?? "");
        Assert.DoesNotContain(templates, t => t.EndsWith("bulk-permission"));
    }

    // --- Enforced: one right, one capability ------------------------------------------------------------------

    [Fact]
    public async Task Enforced_the_share_right_shares_phones_and_nothing_else()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, ShareOnly, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        Assert.True(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkShareDevice), w.E2));
        Assert.False(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkFieldCheckIn), w.E2));

        Assert.Equal(1, Changed(await GrantAsync(w, w.E2, BulkPermission.ShareDevice, true, w.E1)));
        Assert.Equal((true, false), Flags(w, w.E1));
        // Even past the gate (a call made from inside), the field right is asked again over the branch: nothing moves.
        Assert.Equal(0, Changed(await GrantAsync(w, w.E2, BulkPermission.FieldCheckIn, true, w.E1)));
        Assert.Equal((true, false), Flags(w, w.E1));
        // And back off: withdrawal is the same right.
        Assert.Equal(1, Changed(await GrantAsync(w, w.E2, BulkPermission.ShareDevice, false, w.E1)));
        Assert.Equal((false, false), Flags(w, w.E1));
    }

    [Fact]
    public async Task Enforced_the_field_right_grants_field_check_in_and_cannot_share_phones()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, FieldOnly, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        Assert.True(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkFieldCheckIn), w.E2));
        Assert.False(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkShareDevice), w.E2));

        Assert.Equal(1, Changed(await GrantAsync(w, w.E2, BulkPermission.FieldCheckIn, true, w.E1)));
        Assert.Equal((false, true), Flags(w, w.E1));
        Assert.Equal(0, Changed(await GrantAsync(w, w.E2, BulkPermission.ShareDevice, true, w.E1)));
        Assert.Equal((false, true), Flags(w, w.E1));
    }

    [Fact]
    public async Task Enforced_neither_right_is_refused_at_both_doors_and_changes_nothing()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, Neither, w.B1);
        await GiveTemplateAsync(w, w.E4, AccessRoleTemplates.Timekeeper, w.B1);
        await GiveTemplateAsync(w, w.E5, AccessRoleTemplates.StructureOperator, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        foreach (var caller in new[] { w.E2, w.E4, w.E5 })
        {
            Assert.False(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkShareDevice), caller));
            Assert.False(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkFieldCheckIn), caller));
            Assert.Equal(0, Changed(await GrantAsync(w, caller, BulkPermission.ShareDevice, true, w.E1)));
            Assert.Equal(0, Changed(await GrantAsync(w, caller, BulkPermission.FieldCheckIn, true, w.E1)));
        }
        Assert.Equal((false, false), Flags(w, w.E1));
    }

    [Fact]
    public async Task Enforced_both_rights_do_both()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, Both, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        Assert.True(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkShareDevice), w.E2));
        Assert.True(await GateAsync(w, typeof(ManagerController), nameof(ManagerController.BulkFieldCheckIn), w.E2));
        Assert.Equal(1, Changed(await GrantAsync(w, w.E2, BulkPermission.ShareDevice, true, w.E1)));
        Assert.Equal(1, Changed(await GrantAsync(w, w.E2, BulkPermission.FieldCheckIn, true, w.E1)));
        Assert.Equal((true, true), Flags(w, w.E1));
    }

    [Fact]
    public async Task Enforced_a_right_at_one_branch_does_not_reach_a_branch_only_seen()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2 sees B1 and B3 (employees.view at both) but shares phones only at B1.
        await GiveRoleAsync(w, w.E2, [AccessPermissions.EmployeesView], w.B1, w.B3);
        await GiveRoleAsync(w, w.E2, ShareOnly, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var r = await GrantAsync(w, w.E2, BulkPermission.ShareDevice, true, w.E1, w.E3);
        Assert.Equal(1, Changed(r));
        Assert.Equal(1, Skipped(r));
        Assert.Equal((true, false), Flags(w, w.E1));
        Assert.Equal((false, false), Flags(w, w.E3));
    }

    [Fact]
    public async Task Enforced_the_right_is_asked_inside_too_even_when_the_request_carries_none()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, [AccessPermissions.EmployeesView], w.B1, w.B3);
        await GiveRoleAsync(w, w.E2, ShareOnly, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        // A context whose request names no right (no middleware ran): «my branches» is everything E2 sees, B1 and B3.
        // The capability's own right, asked over each branch inside the action, still keeps B3 out.
        var bare = new AuthzObserver(new CountingSink(), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthzObserver>.Instance, new OperatorAllowlist(new HashSet<Guid> { w.Operator }));
        var r = await Desk(w.Context(AccessWorld.TenantA, bare), w, w.E2)
            .BulkShareDevice(new BulkGrantRequest([w.E1, w.E3], true));
        Assert.Equal(1, Changed(r));
        Assert.Equal((false, false), Flags(w, w.E3));
    }

    [Fact]
    public async Task Another_branch_and_another_company_are_never_changed()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, Both, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        foreach (var which in new[] { BulkPermission.ShareDevice, BulkPermission.FieldCheckIn })
        {
            var r = await GrantAsync(w, w.E2, which, true, w.E3, w.OtherCompanyManager);
            Assert.Equal(0, Changed(r));
            Assert.Equal(2, Skipped(r));
        }
        Assert.Equal((false, false), Flags(w, w.E3));
        Assert.Equal((false, false), Flags(w, w.OtherCompanyManager, AccessWorld.TenantB));
    }

    // --- today's managers, in every mode ----------------------------------------------------------------------

    [Fact]
    public async Task Todays_managers_keep_both_capabilities_over_their_own_staff_in_every_mode()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Shadow, AuthzMode.Enforced })
        {
            await SetModeAsync(w, mode);
            var sink = new CountingSink();
            foreach (var which in new[] { BulkPermission.ShareDevice, BulkPermission.FieldCheckIn })
            {
                // M1 manages B1 and B2; E3 is at B3.
                var desk = Desk(Request(w, AdminController.RightFor(which), sink), w, w.M1);
                var request = new BulkGrantRequest([w.E1, w.E2, w.E3], true);
                var on = which == BulkPermission.ShareDevice ? await desk.BulkShareDevice(request) : await desk.BulkFieldCheckIn(request);
                Assert.True(Changed(on) == 2, $"{mode} {which}");
                Assert.Equal(1, Skipped(on));
            }
            Assert.Equal((true, true), Flags(w, w.E1));
            Assert.Equal((true, true), Flags(w, w.E2));
            Assert.Equal((false, false), Flags(w, w.E3));
            if (mode != AuthzMode.Enforced)
                Assert.True(sink.Count == 0, $"{mode}: {sink.Count} kölgə fərqi");

            foreach (var which in new[] { BulkPermission.ShareDevice, BulkPermission.FieldCheckIn })
                Assert.Equal(2, Changed(await GrantAsync(w, w.M1, which, false, w.E1, w.E2)));
        }
    }

    [Fact]
    public async Task Enforced_todays_manager_and_the_company_administrator_pass_both_doors()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await SetModeAsync(w, AuthzMode.Enforced);
        foreach (var action in new[] { nameof(ManagerController.BulkShareDevice), nameof(ManagerController.BulkFieldCheckIn) })
        {
            Assert.True(await GateAsync(w, typeof(ManagerController), action, w.M1));
            Assert.True(await GateAsync(w, typeof(AdminController), action, w.Admin1));
        }
    }

    // --- the admin desk ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_admin_desk_is_the_company_administrators_in_enforced_whatever_the_right()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveRoleAsync(w, w.E2, Both, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        // The admin routes are the company-wide shape: a branch holder of both rights uses the manager's twins.
        foreach (var action in new[] { nameof(AdminController.BulkShareDevice), nameof(AdminController.BulkFieldCheckIn) })
            Assert.False(await GateAsync(w, typeof(AdminController), action, w.E2));
    }

    [Fact]
    public async Task The_admin_desk_grants_both_across_the_company_in_legacy_and_enforced()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Enforced })
        {
            await SetModeAsync(w, mode);
            var share = await AdminDesk(Request(w, AccessPermissions.DevicesShareGrant), w, w.Admin1)
                .BulkShareDevice(new BulkGrantRequest([w.E1, w.E3], true));
            var field = await AdminDesk(Request(w, AccessPermissions.FieldCheckInGrant), w, w.Admin1)
                .BulkFieldCheckIn(new BulkGrantRequest([w.E1, w.E3], true));
            Assert.True(Changed(share) == 2 && Skipped(share) == 0, $"{mode} share");
            Assert.True(Changed(field) == 2 && Skipped(field) == 0, $"{mode} field");
            Assert.Equal((true, true), Flags(w, w.E3));

            await AdminDesk(Request(w, AccessPermissions.DevicesShareGrant), w, w.Admin1).BulkShareDevice(new BulkGrantRequest([w.E1, w.E3], false));
            await AdminDesk(Request(w, AccessPermissions.FieldCheckInGrant), w, w.Admin1).BulkFieldCheckIn(new BulkGrantRequest([w.E1, w.E3], false));
            Assert.Equal((false, false), Flags(w, w.E3));
        }
    }

    [Fact]
    public async Task The_admin_desk_never_reaches_another_company()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await SetModeAsync(w, AuthzMode.Enforced);
        var r = await AdminDesk(Request(w, AccessPermissions.DevicesShareGrant), w, w.Admin1)
            .BulkShareDevice(new BulkGrantRequest([w.OtherCompanyManager], true));
        Assert.Equal(0, Changed(r));
        Assert.Equal((false, false), Flags(w, w.OtherCompanyManager, AccessWorld.TenantB));
    }
}
