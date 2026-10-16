using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Enforced mode (Phase 5): in a company switched to it the permission model DECIDES — the gate of every endpoint
/// that names its permission, and every scope rule. Two things must hold: today's admins and managers get exactly
/// the answers they get today (the switch takes nothing from them), and somebody whose role exists only in the
/// new model reaches what that role says and nothing more. Legacy and Shadow are not touched.
/// </summary>
public class AuthzEnforcedTests
{
    private sealed class NoSink : IAuthzShadowSink
    {
        public Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private static async Task SetModeAsync(AccessWorld w, AuthzMode mode)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = mode;
        await db.SaveChangesAsync();
    }

    /// <summary>A request's context, with its own observer (and so its own 30-second mode cache).</summary>
    private static AppDbContext Decided(AccessWorld w) =>
        w.Context(AccessWorld.TenantA, new AuthzObserver(new NoSink(), new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AuthzObserver>.Instance, new OperatorAllowlist(new HashSet<Guid> { w.Operator })));

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

    private delegate Task<bool> Rule(AppDbContext db, Guid caller, EmployeeRole role, Guid target, CancellationToken ct);

    private static readonly (string Name, Rule Ask)[] Rules =
    [
        ("CanAccess", LocationScopeRules.CanAccessEmployeeAsync),
        ("CanSchedule", LocationScopeRules.CanScheduleEmployeeAsync),
        ("CanManage", LocationScopeRules.CanManageEmployeeAsync),
        ("CorrectAttendance", LocationScopeRules.CanCorrectAttendanceAsync),
    ];

    [Fact]
    public async Task Todays_admins_and_managers_get_the_same_answers_once_the_model_decides()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();

        async Task<Dictionary<string, bool>> AnswersAsync(AuthzMode mode)
        {
            await SetModeAsync(w, mode);
            var answers = new Dictionary<string, bool>();
            foreach (var (name, ask) in Rules)
                foreach (var caller in w.Panel)
                    foreach (var target in w.Everyone)
                    {
                        using var db = Decided(w);
                        answers[$"{name} {w.Name(caller)}→{w.Name(target)}"] =
                            await ask(db, caller, w.RoleOf(caller), target, default);
                    }
            return answers;
        }

        var legacy = await AnswersAsync(AuthzMode.Legacy);
        var enforced = await AnswersAsync(AuthzMode.Enforced);

        var differ = legacy.Where(kv => enforced[kv.Key] != kv.Value).Select(kv => $"{kv.Key}: bu gün {kv.Value}").ToList();
        Assert.True(differ.Count == 0, string.Join("\n", differ));
        Assert.True(legacy.Count > 300, $"only {legacy.Count} answers compared");
    }

    [Fact]
    public async Task A_managers_list_is_narrowed_to_the_same_branches()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await SetModeAsync(w, AuthzMode.Enforced);

        using var db = Decided(w);
        var managed = await LocationScopeRules.ManagedLocationIdsAsync(db, w.M1, default);

        Assert.Equal(new[] { w.B1, w.B2 }.OrderBy(x => x), managed.OrderBy(x => x));
    }

    [Fact]
    public async Task A_timekeeper_who_is_plain_staff_reaches_their_branch_only_when_the_model_decides()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2 works at B2; the company makes them the timekeeper of B1, where E1 works.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);

        await SetModeAsync(w, AuthzMode.Legacy);
        using (var db = Decided(w))
        {
            Assert.False(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E2, EmployeeRole.Employee, w.E1, default));
            Assert.False((await LocationScopeRules.ScopeAsync(db, w.E2, EmployeeRole.Employee, AccessPermissions.EmployeesView, default)).Any);
        }

        await SetModeAsync(w, AuthzMode.Enforced);
        using (var db = Decided(w))
        {
            Assert.True(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E2, EmployeeRole.Employee, w.E1, default));
            Assert.True(await LocationScopeRules.CanScheduleEmployeeAsync(db, w.E2, EmployeeRole.Employee, w.E1, default));
            // The template carries no attendance correction: the role decides, not the panel.
            Assert.False(await LocationScopeRules.CanCorrectAttendanceAsync(db, w.E2, EmployeeRole.Employee, w.E1, default));
            // Their own branch's people only.
            Assert.False(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E2, EmployeeRole.Employee, w.E3, default));
            var scope = await LocationScopeRules.ScopeAsync(db, w.E2, EmployeeRole.Employee, AccessPermissions.EmployeesView, default);
            Assert.False(scope.Company);
            Assert.Equal(new[] { w.B1 }, scope.Branches);
            Assert.Equal(new[] { w.B1 }, await LocationScopeRules.ManagedLocationIdsAsync(db, w.E2, default));
            // Nowhere for a right the role does not carry.
            Assert.False((await LocationScopeRules.ScopeAsync(db, w.E2, EmployeeRole.Employee, AccessPermissions.PayrollView, default)).Any);
        }
    }

    [Fact]
    public async Task Plain_staff_with_no_role_reach_nothing_in_any_mode()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Shadow, AuthzMode.Enforced })
        {
            await SetModeAsync(w, mode);
            using var db = Decided(w);
            Assert.False(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E3, EmployeeRole.Employee, w.E1, default));
            Assert.False((await LocationScopeRules.ScopeAsync(db, w.E3, EmployeeRole.Employee, AccessPermissions.EmployeesView, default)).Any);
            // One's own records stay one's own.
            Assert.True(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E3, EmployeeRole.Employee, w.E3, default));
        }
    }

    // ─────────────────────────── the gate ───────────────────────────

    private static async Task<AuthorizationHandlerContext> GateAsync(
        AccessWorld w, Guid caller, string role, PermissionAttribute permission)
    {
        var db = Decided(w);
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(db).BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", caller.ToString()), new Claim("role", role)], "test")),
        };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(permission), "test"));
        var requirement = new RolesAuthorizationRequirement(["Admin", "Manager"]);
        var context = new AuthorizationHandlerContext([requirement], http.User, http);
        await new PermissionGateHandler(NullLogger<PermissionGateHandler>.Instance).HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task The_gate_lets_the_model_answer_only_in_an_enforced_company()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        var view = new PermissionAttribute(AccessPermissions.EmployeesView);

        // Legacy: the handler stays out of it — the role check alone decides, as today.
        await SetModeAsync(w, AuthzMode.Legacy);
        var legacy = await GateAsync(w, w.E2, "Employee", view);
        Assert.False(legacy.HasSucceeded);
        Assert.False(legacy.HasFailed);

        await SetModeAsync(w, AuthzMode.Enforced);
        Assert.True((await GateAsync(w, w.E2, "Employee", view)).HasSucceeded);       // the timekeeper is let in
        Assert.True((await GateAsync(w, w.E3, "Employee", view)).HasFailed);          // plain staff is not
        Assert.True((await GateAsync(w, w.Admin1, "Admin", view)).HasSucceeded);      // today's admin still is
        Assert.True((await GateAsync(w, w.M1, "Manager", view)).HasSucceeded);        // and today's manager
        // A company-wide endpoint wants the right company-wide: a timekeeper's branch is not enough.
        var companyWide = new PermissionAttribute(AccessPermissions.EmployeesView) { AtCompany = true };
        Assert.True((await GateAsync(w, w.E2, "Employee", companyWide)).HasFailed);
        Assert.True((await GateAsync(w, w.M1, "Manager", companyWide)).HasFailed);
        Assert.True((await GateAsync(w, w.Admin1, "Admin", companyWide)).HasSucceeded);
    }
}
