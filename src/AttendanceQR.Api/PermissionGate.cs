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
        var permission = http.GetEndpoint()?.Metadata.GetMetadata<PermissionAttribute>();
        if (permission is null)
            return;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        if (db.AuthzObserver is not { } observer)
            return;
        var ct = http.RequestAborted;
        try
        {
            if (await observer.ModeAsync(db, ct) != AuthzMode.Enforced)
                return;
            var access = (await AccessGraph.LoadAsync(db, ct)).For(context.User.EmployeeId());
            if (AuthzShadowMiddleware.NewAllows(permission, access))
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
}
