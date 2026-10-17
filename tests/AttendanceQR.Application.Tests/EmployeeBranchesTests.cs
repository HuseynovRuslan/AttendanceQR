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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The branches the staff screen offers (GET /api/manager/employee-branches). Found by the staging E2E suite on
/// 2026-10-17: the form read them from /locations, which asks «locations.view», so a Tabelçi — who adds people but
/// does not hold that — got an empty list and a disabled «İşçi əlavə et». The answer must be their own area only,
/// and must agree with what the save then accepts.
/// </summary>
public class EmployeeBranchesTests
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
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", "Manager")], "test")),
                },
            },
        };

    private static Dictionary<Guid, (bool CanCreate, bool CanMove)> Branches(IActionResult result) =>
        ((System.Collections.IEnumerable)Assert.IsType<OkObjectResult>(result).Value!).Cast<object>().ToDictionary(
            o => (Guid)o.GetType().GetProperty("id")!.GetValue(o)!,
            o => ((bool)o.GetType().GetProperty("canCreate")!.GetValue(o)!, (bool)o.GetType().GetProperty("canMove")!.GetValue(o)!));

    private static async Task<Dictionary<Guid, (bool CanCreate, bool CanMove)>> AskAsync(AccessWorld w, Guid caller) =>
        Branches(await Desk(w, Request(w, AccessPermissions.EmployeesView), caller).EmployeeBranches());

    private static ManagerEmployeeRequest NewPerson(string name, Guid branch) => new(
        FullName: name, Email: $"{Guid.NewGuid():N}@a.local", PhoneNumber: null, FatherName: null, Position: null,
        LocationId: branch);

    [Fact]
    public async Task A_timekeeper_gets_their_own_branch_to_add_people_to_and_nothing_else()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2 (home B2) is the Tabelçi of B1.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var branches = await AskAsync(w, w.E2);
        Assert.Equal(new[] { w.B1 }, branches.Keys);
        Assert.Equal((true, true), branches[w.B1]);
        // Not their home branch, not another company's.
        Assert.DoesNotContain(w.B2, branches.Keys);
        Assert.DoesNotContain(w.OtherBranch, branches.Keys);
    }

    [Fact]
    public async Task What_is_offered_is_exactly_what_the_save_accepts()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var offered = await AskAsync(w, w.E2);
        Assert.True(offered[w.B1].CanCreate);
        var added = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E2).CreateEmployee(NewPerson("Yeni B1", w.B1));
        Assert.IsType<OkObjectResult>(added);

        Assert.False(offered.ContainsKey(w.B3));
        var refused = await Desk(w, Request(w, AccessPermissions.EmployeesCreate), w.E2).CreateEmployee(NewPerson("Yeni B3", w.B3));
        Assert.Equal(StatusCodes.Status403Forbidden, (refused as ObjectResult)?.StatusCode);
    }

    [Fact]
    public async Task An_observer_sees_their_branch_but_may_add_or_move_nobody_there()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E3, AccessRoleTemplates.Observer, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var branches = await AskAsync(w, w.E3);
        Assert.Equal(new[] { w.B1 }, branches.Keys);
        Assert.Equal((false, false), branches[w.B1]);
    }

    [Fact]
    public async Task Todays_managers_get_every_branch_they_manage_in_every_mode()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        foreach (var mode in new[] { AuthzMode.Legacy, AuthzMode.Shadow, AuthzMode.Enforced })
        {
            await SetModeAsync(w, mode);
            // M1 manages B1 and B2.
            var branches = await AskAsync(w, w.M1);
            Assert.Equal(new[] { w.B1, w.B2 }.Order(), branches.Keys.Order());
            Assert.All(branches.Values, b => Assert.Equal((true, true), b));
        }
    }
}
