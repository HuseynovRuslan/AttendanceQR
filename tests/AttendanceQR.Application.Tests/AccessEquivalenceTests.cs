using System.Reflection;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Access;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The owner's condition for the permission model (2026-10-15): no account may gain or lose access by
/// accident. Every one of today's scope rules is asked about every caller and every person in a company built
/// from today's awkward cases — and the permission model, fed only by the migration of today's roles, must
/// answer the same every time. A difference here is printed by name, rule and both answers.
/// </summary>
public class AccessEquivalenceTests
{
    [Fact]
    public async Task Todays_scope_rules_and_the_permission_model_agree_for_every_pair()
    {
        using var w = new AccessWorld();
        var graph = await w.ReconcileAndLoadAsync();
        var db = w.Context(AccessWorld.TenantA);
        var homes = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.LocationId);
        var mismatches = new List<string>();

        foreach (var caller in w.Everyone)
            foreach (var target in w.Everyone)
            {
                var role = w.RoleOf(caller);
                Check(ShadowRules.CanAccess, await LocationScopeRules.CanAccessEmployeeAsync(db, caller, role, target, default), caller, target);
                Check(ShadowRules.CanSchedule, await LocationScopeRules.CanScheduleEmployeeAsync(db, caller, role, target, default), caller, target);
                Check(ShadowRules.CanManage, await LocationScopeRules.CanManageEmployeeAsync(db, caller, role, target, default), caller, target);
            }

        Assert.Empty(mismatches);

        void Check(string rule, bool legacy, Guid caller, Guid target)
        {
            var decided = ShadowRules.Decide(rule, graph.For(caller), target, homes[target], graph.For(target), target == w.Operator);
            if (decided != legacy)
                mismatches.Add($"{rule} {w.Name(caller)} → {w.Name(target)}: bu gün {legacy}, yeni {decided}");
        }
    }

    [Fact]
    public async Task The_manager_desks_own_rules_agree_too()
    {
        using var w = new AccessWorld();
        var graph = await w.ReconcileAndLoadAsync();
        var homes = await w.Context(AccessWorld.TenantA).Employees.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.LocationId);
        var mismatches = new List<string>();

        foreach (var manager in new[] { w.M1, w.M2, w.M3, w.M4, w.M5 })
            foreach (var target in w.Everyone)
            {
                foreach (var (rule, method) in new[]
                {
                    (ShadowRules.CredentialTarget, "LegacyCredentialTargetAsync"),
                    (ShadowRules.ManageableEmployee, "LegacyManageableEmployeeAsync"),
                    (ShadowRules.LeaveSubject, "LegacyLeaveSubjectAsync"),
                })
                {
                    var legacy = await TodayAsync(w.ManagerDesk(manager), method, target);
                    var decided = ShadowRules.Decide(rule, graph.For(manager), target, homes[target], graph.For(target), target == w.Operator);
                    if (decided != legacy)
                        mismatches.Add($"{rule} {w.Name(manager)} → {w.Name(target)}: bu gün {legacy}, yeni {decided}");
                }
            }

        Assert.Empty(mismatches);
    }

    [Fact]
    public async Task A_managers_branches_are_the_same_set()
    {
        using var w = new AccessWorld();
        var graph = await w.ReconcileAndLoadAsync();
        var db = w.Context(AccessWorld.TenantA);

        foreach (var manager in new[] { w.M1, w.M2, w.M3, w.M4, w.M5 })
        {
            var today = await LocationScopeRules.ManagedLocationIdsAsync(db, manager, default);
            Assert.True(graph.For(manager).BranchesFor(AccessPermissions.EmployeesView)!.SetEquals(today), w.Name(manager));
        }
    }

    [Fact]
    public async Task Another_companys_people_hold_nothing_here()
    {
        using var w = new AccessWorld();
        await AccessSeeder.ReconcileTenantAsync(w.Context(AccessWorld.TenantB), AccessWorld.TenantB);
        var graph = await w.ReconcileAndLoadAsync();

        Assert.Empty(graph.For(w.OtherCompanyManager).Grants);
    }

    /// <summary>Today's private manager-desk rule, called as it is — allowed when it hands back the person.</summary>
    private static async Task<bool> TodayAsync(ManagerController desk, string method, Guid target)
    {
        var mi = typeof(ManagerController).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var args = mi.GetParameters().Length == 1 ? new object?[] { target } : [target, new NotFoundResult()];
        var task = (Task)mi.Invoke(desk, args)!;
        await task;
        var tuple = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return tuple.GetType().GetField("Item1")!.GetValue(tuple) is not null;
    }
}
