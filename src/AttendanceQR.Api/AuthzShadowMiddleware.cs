using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Services.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace AttendanceQR.Api;

/// <summary>
/// The gate half of Shadow mode (docs/strukturlar-ve-selahiyyetler.md §4.3). For a company in Shadow mode it
/// asks, for every endpoint that carries a [Permission], both today's gate (the endpoint's
/// [Authorize(Roles)], or its PermissionAttribute.LegacyRoles) and the permission model whether this caller
/// may call it, and records any disagreement. It decides nothing — the request goes on exactly as before.
/// The scope half is AuthzObserver, which hears LocationScopeRules.
/// </summary>
public sealed class AuthzShadowMiddleware
{
    private readonly RequestDelegate _next;

    public AuthzShadowMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context, AppDbContext db, IMemoryCache cache, IAuthzShadowSink sink, ILogger<AuthzShadowMiddleware> logger)
    {
        await ObserveAsync(context, db, cache, sink, logger);
        await _next(context);
    }

    private static async Task ObserveAsync(
        HttpContext context, AppDbContext db, IMemoryCache cache, IAuthzShadowSink sink, ILogger logger)
    {
        var ct = context.RequestAborted;
        try
        {
            if (context.User.Identity?.IsAuthenticated != true)
                return;
            var endpoint = context.GetEndpoint();
            var permission = endpoint?.Metadata.GetMetadata<PermissionAttribute>();
            if (endpoint is null || permission is null)
                return;
            if (await AuthzObserver.ModeAsync(db, cache, ct) != AuthzMode.Shadow)
                return;

            var legacy = LegacyAllows(endpoint.Metadata.GetOrderedMetadata<AuthorizeAttribute>(), permission, context.User);
            var access = (await AccessGraph.LoadAsync(db, ct)).For(context.User.EmployeeId());
            var decided = NewAllows(permission, access);
            if (decided == legacy)
                return;

            var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? endpoint.DisplayName ?? "?";
            await sink.RecordAsync(db, new ShadowRecord(
                db.CurrentTenantId, permission.Twin ? "Twin" : "Gate", $"{context.Request.Method} {route}",
                context.User.EmployeeId(), string.Empty, legacy, decided), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Authz shadow: gate could not be compared");
        }
    }

    /// <summary>Today's gate. Every [Authorize(Roles)] on the class and the action must pass — that is how
    /// ASP.NET combines them — unless the permission names the roles an in-action check lets through.</summary>
    public static bool LegacyAllows(IEnumerable<AuthorizeAttribute> authorize, PermissionAttribute permission, ClaimsPrincipal user)
    {
        var role = user.FindFirstValue("role");
        if (permission.LegacyRoles is { } roles)
            return InRoles(role, roles);
        return authorize.All(a => string.IsNullOrWhiteSpace(a.Roles) || InRoles(role, a.Roles));
    }

    /// <summary>The permission model's gate: the permission held anywhere, or company-wide for a company-wide
    /// endpoint. Where in the company is the scope checks' question, not the gate's.</summary>
    public static bool NewAllows(PermissionAttribute permission, EffectiveAccess access) =>
        permission.AtCompany ? access.HasAtCompany(permission.Key) : access.Has(permission.Key);

    /// <summary>The same two attribute sets for a controller action found by reflection — what the tests use.</summary>
    public static IEnumerable<AuthorizeAttribute> AuthorizeAttributesOf(MethodInfo action) =>
        action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

    public static PermissionAttribute? PermissionOf(MethodInfo action) =>
        action.GetCustomAttribute<PermissionAttribute>(inherit: true)
        ?? action.DeclaringType!.GetCustomAttribute<PermissionAttribute>(inherit: true);

    private static bool InRoles(string? role, string roles) =>
        role is not null && roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains(role, StringComparer.Ordinal);
}
