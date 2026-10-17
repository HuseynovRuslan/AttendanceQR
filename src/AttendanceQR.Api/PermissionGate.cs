using System.Security.Claims;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace AttendanceQR.Api;

/// <summary>
/// The gate of a company switched to Enforced (Phase 5 of docs/strukturlar-ve-selahiyyetler.md): on an endpoint
/// that names its permission, the permission model — not the role in the token — answers its
/// [Authorize(Roles)]. A timekeeper who is plain staff by today's role passes where their role in the new model
/// reaches; an admin whose company role was taken away does not.
///
/// What «the model allows» means is <see cref="Allows"/>. An endpoint only an admin may call today is the
/// company-wide shape of something (the admin's staff list, with salaries and roles) or a company-wide act (a
/// holiday, an import): it stays the Company administrator's, unless it is marked
/// <see cref="PermissionAttribute.Scoped"/> — it narrows itself to the caller's area. Everything else opens to
/// whoever holds the permission (company-wide, for <see cref="PermissionAttribute.AtCompany"/>).
///
/// For a company in Legacy or Shadow mode it does nothing at all: today's role check decides exactly as before.
/// An endpoint with no [Permission] (the platform console, the employee's own screens) is never touched.
/// Where the model cannot be read the answer is «no» — a gate that fails open is not a gate.
/// </summary>
public sealed class PermissionGateHandler : AuthorizationHandler<RolesAuthorizationRequirement>
{
    private readonly ILogger<PermissionGateHandler> _logger;

    public PermissionGateHandler(ILogger<PermissionGateHandler> logger) => _logger = logger;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
    {
        if (context.Resource is not HttpContext http || context.User.Identity?.IsAuthenticated != true)
            return;
        var endpoint = http.GetEndpoint();
        var permission = endpoint?.Metadata.GetMetadata<PermissionAttribute>();
        if (endpoint is null || permission is null)
            return;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        if (db.AuthzObserver is not { } observer)
            return;
        var ct = http.RequestAborted;
        try
        {
            if (await observer.ModeAsync(db, ct) != AuthzMode.Enforced)
                return;
            var access = (await observer.GraphAsync(db, ct)).For(context.User.EmployeeId());
            if (Allows(endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>(), permission, access))
                context.Succeed(requirement);
            else
                context.Fail();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Authz enforced: gate for {Permission} could not be decided — refused", permission.Key);
            context.Fail();
        }
    }

    /// <summary>The Enforced gate of one endpoint, for one person — see the class.</summary>
    public static bool Allows(IEnumerable<AuthorizeAttribute> authorize, PermissionAttribute permission, EffectiveAccess access) =>
        AdminOnlyToday(authorize, permission) && !permission.Scoped
            ? AccessProjection.IsCompanyAdmin(access)
            : AuthzShadowMiddleware.NewAllows(permission, access);

    /// <summary>Today only an admin gets through: a role attribute (class or action) without Manager, or an
    /// in-action gate (<see cref="PermissionAttribute.LegacyRoles"/>) without it.</summary>
    public static bool AdminOnlyToday(IEnumerable<AuthorizeAttribute> authorize, PermissionAttribute permission) =>
        permission.LegacyRoles is { } roles
            ? !HasManager(roles)
            : authorize.Any(a => !string.IsNullOrWhiteSpace(a.Roles) && !HasManager(a.Roles));

    private static bool HasManager(string roles) =>
        roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(nameof(EmployeeRole.Manager), StringComparer.Ordinal);
}

/// <summary>
/// The request's role in a company switched to Enforced: what the permission model makes of this person for
/// the endpoint's permission (<see cref="AccessProjection"/>), written over the token's role before
/// authorization and before any action reads <c>User.Role()</c>. So every check written as «a manager is
/// narrowed to their branches» narrows a timekeeper too — to the branches the model gives them — instead of
/// reading «not a manager» as «an admin»; and the endpoint's permission becomes the request's
/// (<see cref="IAuthzObserver.RequestPermissions"/>), so «my branches» means the branches where this right is
/// held, and the projected role is the request's ceiling (<see cref="IAuthzObserver.RoleCeiling"/>) — nothing
/// called inside it can make it more. The token's own role is kept beside it as «token_role».
///
/// Legacy and Shadow: the role is not touched; only the request's permission is noted, for the shadow to
/// measure the rules Enforced will use. Endpoints with no [Permission] are never touched.
/// </summary>
public sealed class AuthzEnforcedMiddleware
{
    public const string TokenRoleClaim = "token_role";

    private readonly RequestDelegate _next;

    public AuthzEnforcedMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.GetEndpoint()?.Metadata.GetMetadata<PermissionAttribute>() is { } permission
            && db.AuthzObserver is { } observer)
        {
            observer.AddRequestPermission(permission.Key);
            var tokenRole = context.User.FindFirstValue("role");
            if (Enum.TryParse<EmployeeRole>(tokenRole, out var role)
                && await observer.ModeAsync(db, context.RequestAborted) == AuthzMode.Enforced)
            {
                // Unreadable in Enforced: plain staff.
                var projected = await observer.ProjectRoleAsync(
                    db, context.User.EmployeeId(), role, permission.Key, context.RequestAborted);
                observer.RoleCeiling = projected;
                if (projected != role)
                    context.User = WithRole(context.User, projected, tokenRole!);
            }
        }
        await _next(context);
    }

    public static ClaimsPrincipal WithRole(ClaimsPrincipal user, EmployeeRole role, string tokenRole)
    {
        var identity = user.Identity as ClaimsIdentity;
        var claims = user.Claims.Where(c => c.Type != "role").ToList();
        claims.Add(new Claim("role", role.ToString()));
        claims.Add(new Claim(TokenRoleClaim, tokenRole));
        return new ClaimsPrincipal(new ClaimsIdentity(
            claims, identity?.AuthenticationType ?? "Bearer", identity?.NameClaimType ?? "sub", identity?.RoleClaimType ?? "role"));
    }
}
