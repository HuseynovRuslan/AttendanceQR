using System.Security.Claims;

namespace AttendanceQR.Api;

/// <summary>
/// «Baxış rejimi» — a session that may READ a company and change nothing in it.
///
/// It exists because the group head of the customer's companies asked to see all of them, and the
/// cross-company console only had totals: headcount, on-duty, an attendance percentage. The detail he
/// actually wanted — who is absent today, the reports, the tabel — lives in each tenant's own admin
/// panel, and the only door into that was impersonation: a session that can do ANYTHING inside the
/// company, including approve leave, reset PINs and edit attendance.
///
/// So the door was widened by taking the writes off it instead of by rebuilding every screen. A view
/// token carries <c>ro</c>, and this gate refuses every mutating request that arrives with it.
///
/// WHY ONE BLANKET GATE RATHER THAN PER-ENDPOINT CHECKS: there are 113 mutating endpoints today and
/// there will be more next week. A per-endpoint rule is a rule somebody forgets on the endpoint that
/// matters; a method-level gate covers everything written from now on, including code nobody thought
/// about while designing this. New endpoints are safe by default — the opposite of the usual
/// allowlist drift.
/// </summary>
public static class ViewOnlyBoundary
{
    /// <summary>Set by <c>JwtService</c> on a view-only token; its presence is the whole trigger.</summary>
    public const string Claim = "ro";

    /// <summary>
    /// The ONLY mutating-by-method requests a viewer may make: POSTs that write nothing.
    ///
    /// Exactly one qualifies. <c>/api/reports/export-day</c> formats rows the client already has into
    /// a spreadsheet and touches no table — blocking it would take the export button away from a
    /// person whose entire purpose here is to read reports.
    ///
    /// Deliberately NOT here, though both are POSTs that look harmless:
    ///   • <c>/api/admin/reports/generate</c> — it calls GenerateForDateAsync, which REWRITES that
    ///     day's summaries for the whole company. A "report" that rewrites attendance is a write.
    ///   • <c>/api/admin/employees/parse-xlsx</c> — it only parses an upload, but it is step one of
    ///     the bulk import, and a viewer has no business starting an import flow at all.
    /// </summary>
    private static readonly string[] ReadOnlyPosts = ["/api/reports/export-day"];

    public static bool IsViewOnly(this ClaimsPrincipal user) =>
        user.FindFirst(Claim)?.Value == "1";

    /// <summary>Safe methods plus the narrow POST exception. Everything else is refused.</summary>
    public static bool IsAllowed(string method, PathString path)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return true;

        return HttpMethods.IsPost(method)
               && ReadOnlyPosts.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
    }

    public static IApplicationBuilder UseViewOnlyBoundary(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.IsViewOnly() && !IsAllowed(context.Request.Method, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "ViewOnlySession" });
                return;
            }

            await next();
        });
}
