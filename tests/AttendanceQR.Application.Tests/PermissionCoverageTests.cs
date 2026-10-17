using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Api;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Every endpoint today's roles guard names the permission it stands for — so an endpoint added next year
/// without one fails here, not silently in Shadow mode — and, for the three kinds of account that exist today
/// (an admin, a branch manager, plain staff), today's gate and the permission gate give the same answer. The
/// only allowed differences are a twin route — the manager's own copy of an admin endpoint, or the reverse — and
/// an admin-only endpoint marked Scoped, which narrows its own answer to the caller's area.
/// </summary>
public class PermissionCoverageTests
{
    private static IEnumerable<MethodInfo> Actions() =>
        typeof(AdminController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static bool GuardedByRole(MethodInfo action) =>
        AuthzShadowMiddleware.AuthorizeAttributesOf(action).Any(a => !string.IsNullOrWhiteSpace(a.Roles));

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    [Fact]
    public void Every_endpoint_today_guards_by_role_names_its_permission()
    {
        var missing = Actions()
            .Where(a => a.DeclaringType != typeof(SuperAdminController))
            .Where(a => GuardedByRole(a) && AuthzShadowMiddleware.PermissionOf(a) is null)
            .Select(Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void The_platform_console_stays_out_of_the_tenant_permission_model()
    {
        Assert.DoesNotContain(Actions().Where(a => a.DeclaringType == typeof(SuperAdminController)),
            a => AuthzShadowMiddleware.PermissionOf(a) is not null);
    }

    [Fact]
    public void Todays_gate_and_the_permission_gate_agree_for_admins_managers_and_staff()
    {
        var branch = Guid.NewGuid();
        var archetypes = new (string Role, EffectiveAccess Access)[]
        {
            ("Admin", Access(Grant(AccessRoleTemplates.CompanyAdminTemplate, AccessScopeType.Company))),
            ("Manager", Access(
                Grant(AccessRoleTemplates.LegacyManagerTemplate, AccessScopeType.Branch, branch),
                Grant(AccessRoleTemplates.PinHelperTemplate, AccessScopeType.Company))),
            ("Employee", Access()),
        };
        var mismatches = new List<string>();

        foreach (var action in Actions())
        {
            if (AuthzShadowMiddleware.PermissionOf(action) is not { } permission)
                continue;
            foreach (var (role, access) in archetypes)
            {
                var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", role)], "test"));
                var legacy = AuthzShadowMiddleware.LegacyAllows(AuthzShadowMiddleware.AuthorizeAttributesOf(action), permission, user);
                var decided = AuthzShadowMiddleware.NewAllows(permission, access);
                // A twin route, or an admin-only endpoint that narrows itself (Scoped — the bell): a «yes» where
                // today says no is the manager's own copy, not a new door.
                if (legacy == decided || ((permission.Twin || permission.Scoped) && decided && !legacy))
                    continue;
                mismatches.Add($"{Name(action)} as {role}: bu gün {legacy}, yeni {decided}");
            }
        }

        Assert.Empty(mismatches);
    }

    private static ResolvedGrant Grant(AccessRoleTemplate template, AccessScopeType scope, Guid? branch = null) =>
        new(Guid.NewGuid(), template.Key, template.PeersManageable, template.Permissions.ToHashSet(), scope, null,
            branch is Guid b ? new HashSet<Guid> { b } : new HashSet<Guid>());

    private static EffectiveAccess Access(params ResolvedGrant[] grants) => new(Guid.NewGuid(), grants);
}
