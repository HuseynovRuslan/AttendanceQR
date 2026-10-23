using System.Security.Claims;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Shadow mode (docs/strukturlar-ve-selahiyyetler.md §4.3): today's rule keeps deciding, the permission model
/// decides alongside, and only a disagreement is recorded — in Shadow mode only, never in Legacy.
/// </summary>
public class AuthzShadowTests
{
    private sealed class CapturingSink : IAuthzShadowSink
    {
        public List<ShadowRecord> Records { get; } = [];
        public Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private static async Task SetModeAsync(AccessWorld w, AuthzMode mode)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = mode;
        await db.SaveChangesAsync();
    }

    private static (AppDbContext Db, CapturingSink Sink) Observed(AccessWorld w)
    {
        var sink = new CapturingSink();
        var observer = new AuthzObserver(sink, new MemoryCache(new MemoryCacheOptions()), NullLogger<AuthzObserver>.Instance,
            new OperatorAllowlist(new HashSet<Guid> { w.Operator }));
        return (w.Context(AccessWorld.TenantA, observer), sink);
    }

    [Fact]
    public async Task When_both_agree_nothing_is_recorded_and_todays_answer_is_returned()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await SetModeAsync(w, AuthzMode.Shadow);
        var (db, sink) = Observed(w);

        Assert.True(await LocationScopeRules.CanManageEmployeeAsync(db, w.M1, EmployeeRole.Manager, w.E2, default));
        Assert.False(await LocationScopeRules.CanManageEmployeeAsync(db, w.M1, EmployeeRole.Manager, w.Admin1, default));

        Assert.Empty(sink.Records);
    }

    [Fact]
    public async Task A_disagreement_is_recorded_and_todays_answer_still_decides()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // Break the mirror by hand: M1 loses B2 in the permission model but still manages it today.
        using (var tamper = w.Context(AccessWorld.TenantA))
        {
            var managerRole = tamper.AccessRoles.Single(r => r.TemplateKey == AccessRoleTemplates.LegacyManager).Id;
            tamper.RoleAssignments.Single(a => a.EmployeeId == w.M1 && a.AccessRoleId == managerRole && a.LocationId == w.B2)
                .RevokedAtUtc = DateTime.UtcNow;
            tamper.SaveChanges();
        }
        await SetModeAsync(w, AuthzMode.Shadow);
        var (db, sink) = Observed(w);

        var allowed = await LocationScopeRules.CanManageEmployeeAsync(db, w.M1, EmployeeRole.Manager, w.E2, default);

        Assert.True(allowed); // today's answer
        var record = Assert.Single(sink.Records, r => r.Kind == ShadowRules.CanManage);
        Assert.Equal((w.M1, w.E2.ToString(), true, false), (record.EmployeeId, record.TargetKey, record.Legacy, record.New));

        // The branch list a manager's screens are filtered by is compared where the screens ask for it.
        Assert.Equal(2, (await LocationScopeRules.ManagedLocationIdsAsync(db, w.M1, default)).Count);
        Assert.Contains(sink.Records, r => r.Kind == ShadowRules.ManagedSet && r.EmployeeId == w.M1);
    }

    [Fact]
    public async Task In_legacy_mode_nothing_is_compared()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        using (var tamper = w.Context(AccessWorld.TenantA))
        {
            foreach (var a in tamper.RoleAssignments.Where(a => a.EmployeeId == w.M1))
                a.RevokedAtUtc = DateTime.UtcNow;
            tamper.SaveChanges();
        }
        var (db, sink) = Observed(w); // the company is still Legacy

        Assert.True(await LocationScopeRules.CanManageEmployeeAsync(db, w.M1, EmployeeRole.Manager, w.E2, default));
        Assert.Empty(sink.Records);
    }

    [Fact]
    public async Task Me_access_answers_from_the_database_with_the_branches()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        var me = new MeController(w.Context(AccessWorld.TenantA), new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", w.M1.ToString()), new Claim("role", "Manager")], "test")),
                },
            },
        };

        var ok = Assert.IsType<OkObjectResult>(await me.Access());
        var permissions = ((System.Collections.IEnumerable)ok.Value!.GetType().GetProperty("permissions")!.GetValue(ok.Value)!)
            .Cast<object>()
            .ToDictionary(
                p => (string)p.GetType().GetProperty("key")!.GetValue(p)!,
                p => (Company: (bool)p.GetType().GetProperty("company")!.GetValue(p)!,
                      Branches: ((Guid[])p.GetType().GetProperty("locationIds")!.GetValue(p)!).ToHashSet()));

        Assert.True(permissions[AccessPermissions.EmployeesResetPin].Company);           // PIN helper
        Assert.True(permissions[AccessPermissions.AttendanceCorrect].Branches.SetEquals([w.B1, w.B2]));
        Assert.False(permissions.ContainsKey(AccessPermissions.EmployeesDelete));
    }
}
