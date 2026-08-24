namespace AttendanceQR.Api;

/// <summary>
/// Keeps a support impersonation on the console side of the app: it may read, but it may only WRITE
/// through the admin surfaces.
///
/// An impersonation token's "sub" is the customer's admin. Every write it makes is therefore filed
/// under a real person in the customer's company, and some of those writes are not undoable. The one
/// that matters most: the admin panel's sidebar carries an "İşçi rejimi (skan)" link to the employee
/// shell, which redirects itself to the scanner — and a single check-in there would write an
/// attendance record and a selfie under the borrowed admin's id, in the customer's tenant, with the
/// OPERATOR's face in it. Worse, <c>PhotoUploadWorker</c> promotes the first check-in selfie to the
/// permanent reference photo when an account has none, and a freshly created admin has none: the
/// operator's face would silently become that admin's face-audit baseline, forever, and every later
/// face match for that person would be measured against a stranger.
///
/// Nothing in AttendanceController or FieldVisitController checked for this, because impersonation
/// was built for looking at a company, not for building one. Now that the operator is expected to set
/// a company up before handing it over, the operator will be inside a customer's admin session for an
/// hour at a time, and "do not click that link" is not a safeguard.
///
/// The rule is deliberately not a list of forbidden endpoints. A denylist is open until somebody
/// remembers to add to it, and the thing being protected here is a customer's biometric baseline. So:
/// reads pass, and writes pass only under the console prefixes below. A new employee-facing endpoint
/// added next year is refused without anybody touching this file — which is the same shape as
/// <see cref="TemporaryPinGate"/> and as the tenant filter, and for the same reason.
/// </summary>
public static class ImpersonationBoundary
{
    /// <summary>
    /// Where an impersonation session may write. These are the surfaces the admin panel uses to run a
    /// company: staff, branches, positions, shifts, the calendar, leaves, reports, branding.
    ///
    /// /api/auth is here for the ordinary session routes; the three that would hand the operator the
    /// borrowed account's actual credential (set-initial-pin, change-password, and the admin-side
    /// reset-pin/reinvite) refuse an impersonation session on their own, and are tested there.
    /// </summary>
    private static readonly string[] ConsoleWritePrefixes =
    [
        "/api/admin",
        "/api/manager",
        "/api/reports",
        "/api/tenant",
        "/api/tasks",
        "/api/auth",
        // Mixed: a manager assigns, cancels and force-closes visits here (support work), but a worker
        // also records their own visit here. The worker half is carved back out below.
        "/api/field-visits",
    ];

    /// <summary>
    /// The half of /api/field-visits where somebody records their OWN visit — a GPS position, a
    /// selfie at arrival and departure, photos of the work. Filed under the borrowed admin exactly
    /// like a scan, so it is refused for the same reason.
    /// </summary>
    private static bool IsWorkerFieldVisitAction(PathString path)
    {
        if (!path.HasValue) return false;
        var value = path.Value!;
        return value.EndsWith("/api/field-visits/start", StringComparison.OrdinalIgnoreCase)
               || value.EndsWith("/check-in", StringComparison.OrdinalIgnoreCase)
               || value.EndsWith("/check-out", StringComparison.OrdinalIgnoreCase)
               || value.EndsWith("/work-photo", StringComparison.OrdinalIgnoreCase)
               || value.Contains("/checklist/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads are always allowed — support is mostly looking at things.</summary>
    private static bool IsRead(string method)
        => HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    /// <summary>
    /// The whole decision, in one testable function: may an impersonation session make this request?
    /// </summary>
    public static bool IsAllowed(string method, PathString path)
    {
        if (IsRead(method)) return true;
        if (IsWorkerFieldVisitAction(path)) return false;
        return ConsoleWritePrefixes.Any(p => path.StartsWithSegments(p));
    }

    /// <summary>
    /// 403 + <c>NotDuringImpersonation</c> — the same error code AdminController already returns when
    /// an impersonation session reaches for a credential, so the frontend has one string to know.
    /// </summary>
    public static IApplicationBuilder UseImpersonationBoundary(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.IsImpersonating() && !IsAllowed(context.Request.Method, context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "NotDuringImpersonation" });
                return;
            }

            await next();
        });
}
