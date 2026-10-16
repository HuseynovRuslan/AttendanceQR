using System.Reflection;
using System.Security.Claims;
using AttendanceQR.Api;
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
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Phase 5, operation by operation: what every endpoint answers once a company is switched to Enforced, for
/// today's two roles and for the three new ones (structure operator, timekeeper, observer) — and the places where
/// the model now decides INSIDE a request: the role an action reads, the branches a list narrows to, the per-row
/// answers of the manager desk, the PIN queue, the selfie, the bell and the staff alerts.
/// </summary>
public class AuthzEnforcedMatrixTests
{
    // ─────────────────────────── the gate, endpoint by endpoint ───────────────────────────

    private static IEnumerable<MethodInfo> Actions() =>
        typeof(AdminController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());

    private static string Name(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    private static bool Writes(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).Any(m => m != "GET");

    private static ResolvedGrant Grant(AccessRoleTemplate template, AccessScopeType scope, Guid? branch = null) =>
        new(Guid.NewGuid(), template.Key, template.PeersManageable, template.Permissions.ToHashSet(), scope, null,
            branch is Guid b ? new HashSet<Guid> { b } : new HashSet<Guid>());

    private static EffectiveAccess Access(params ResolvedGrant[] grants) => new(Guid.NewGuid(), grants);

    private static readonly Guid Branch = Guid.NewGuid();

    private static (string Role, EffectiveAccess Access)[] TodaysRoles =>
    [
        ("Admin", Access(Grant(AccessRoleTemplates.CompanyAdminTemplate, AccessScopeType.Company))),
        ("Manager", Access(
            Grant(AccessRoleTemplates.LegacyManagerTemplate, AccessScopeType.Branch, Branch),
            Grant(AccessRoleTemplates.PinHelperTemplate, AccessScopeType.Company))),
        ("Employee", Access()),
    ];

    [Fact]
    public void The_enforced_gate_takes_nothing_from_todays_admins_and_managers_and_gives_staff_nothing()
    {
        var differ = new List<string>();
        var checkedCount = 0;
        foreach (var action in Actions())
        {
            if (AuthzShadowMiddleware.PermissionOf(action) is not { } permission)
                continue;
            var authorize = AuthzShadowMiddleware.AuthorizeAttributesOf(action).ToList();
            foreach (var (role, access) in TodaysRoles)
            {
                var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", role)], "test"));
                var today = AuthzShadowMiddleware.LegacyAllows(authorize, permission, user);
                var enforced = PermissionGateHandler.Allows(authorize, permission, access);
                checkedCount++;
                // The two additions the switch makes on purpose: a twin route the other role has its own copy of
                // (an admin may call the manager's), and a Scoped endpoint that narrows itself (the bell).
                if (today == enforced || (enforced && !today && (permission.Twin || permission.Scoped || role == "Admin")))
                    continue;
                differ.Add($"{Name(action)} as {role}: bu gün {today}, Enforced {enforced}");
            }
        }

        Assert.True(differ.Count == 0, string.Join("\n", differ));
        Assert.True(checkedCount > 300, $"only {checkedCount} answers compared");
    }

    private static (string Role, EffectiveAccess Access)[] NewRoles =>
    [
        ("Struktur operatoru", Access(Grant(AccessRoleTemplates.StructureOperatorTemplate, AccessScopeType.Branch, Branch))),
        ("Tabelçi", Access(Grant(AccessRoleTemplates.TimekeeperTemplate, AccessScopeType.Branch, Branch))),
        ("Müşahidəçi", Access(Grant(AccessRoleTemplates.ObserverTemplate, AccessScopeType.Branch, Branch))),
        // An observer over the whole company is still an observer: company-wide reach, not the admin's shape.
        ("Şirkət müşahidəçisi", Access(Grant(AccessRoleTemplates.ObserverTemplate, AccessScopeType.Company))),
    ];

    [Fact]
    public void A_new_role_passes_exactly_where_its_permissions_reach_and_never_an_admin_only_endpoint()
    {
        var wrong = new List<string>();
        foreach (var action in Actions())
        {
            if (AuthzShadowMiddleware.PermissionOf(action) is not { } permission)
                continue;
            var authorize = AuthzShadowMiddleware.AuthorizeAttributesOf(action).ToList();
            var adminOnly = PermissionGateHandler.AdminOnlyToday(authorize, permission) && !permission.Scoped;
            foreach (var (role, access) in NewRoles)
            {
                var expected = !adminOnly && AuthzShadowMiddleware.NewAllows(permission, access);
                if (PermissionGateHandler.Allows(authorize, permission, access) != expected)
                    wrong.Add($"{Name(action)} as {role}: gözlənilən {expected}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    [Fact]
    public void An_observer_passes_no_endpoint_that_changes_anything()
    {
        var writes = new List<string>();
        foreach (var action in Actions().Where(Writes))
        {
            if (AuthzShadowMiddleware.PermissionOf(action) is not { } permission)
                continue;
            var authorize = AuthzShadowMiddleware.AuthorizeAttributesOf(action).ToList();
            foreach (var (role, access) in NewRoles.Where(r => r.Role.Contains("üşahidəçi")))
                if (PermissionGateHandler.Allows(authorize, permission, access))
                    writes.Add($"{Name(action)} as {role}");
        }
        Assert.True(writes.Count == 0, string.Join("\n", writes));
    }

    [Fact]
    public void A_timekeeper_reaches_the_daily_work_of_their_branch_and_nothing_that_corrects_pay()
    {
        var timekeeper = NewRoles.Single(r => r.Role == "Tabelçi").Access;
        bool Passes(string controller, string action)
        {
            var method = Actions().Single(a => a.DeclaringType!.Name == controller && a.Name == action);
            return PermissionGateHandler.Allows(AuthzShadowMiddleware.AuthorizeAttributesOf(method).ToList(),
                AuthzShadowMiddleware.PermissionOf(method)!, timekeeper);
        }

        // Their people, their PINs, their shifts, their leave, a new phone.
        Assert.True(Passes(nameof(ManagerController), nameof(ManagerController.Employees)));
        Assert.True(Passes(nameof(ManagerController), nameof(ManagerController.CreateEmployee)));
        Assert.True(Passes(nameof(ManagerController), nameof(ManagerController.UpdateEmployee)));
        Assert.True(Passes(nameof(ManagerController), nameof(ManagerController.ResetPin)));
        Assert.True(Passes(nameof(ManagerController), nameof(ManagerController.CreateLeave)));
        Assert.True(Passes(nameof(ShiftOverridesController), nameof(ShiftOverridesController.Set)));
        Assert.True(Passes(nameof(AdminDeviceChangeController), nameof(AdminDeviceChangeController.Approve)));
        Assert.True(Passes(nameof(AdminNotificationsController), nameof(AdminNotificationsController.Badges)));
        // Not theirs: a day's correction, voiding a day, pay, the admin's company-wide shapes.
        Assert.False(Passes(nameof(AdminAttendanceController), nameof(AdminAttendanceController.Update)));
        Assert.False(Passes(nameof(AdminAttendanceController), nameof(AdminAttendanceController.VoidFraud)));
        Assert.False(Passes(nameof(ReportsController), nameof(ReportsController.Payroll)));
        Assert.False(Passes(nameof(AdminController), nameof(AdminController.List)));
        Assert.False(Passes(nameof(AdminDeviceBindingController), nameof(AdminDeviceBindingController.Revoke)));
    }

    // ─────────────────────────── the role an action reads ───────────────────────────

    private static async Task SetModeAsync(AccessWorld w, AuthzMode mode)
    {
        using var db = w.Context(AccessWorld.TenantA);
        db.Tenants.Single(t => t.Id == AccessWorld.TenantA).AuthzMode = mode;
        await db.SaveChangesAsync();
    }

    private sealed class NoSink : IAuthzShadowSink
    {
        public Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private static AuthzObserver Observer(AccessWorld w) =>
        new(new NoSink(), new MemoryCache(new MemoryCacheOptions()), NullLogger<AuthzObserver>.Instance,
            new OperatorAllowlist(new HashSet<Guid> { w.Operator }));

    private static AppDbContext Decided(AccessWorld w, string? requestPermission = null)
    {
        var observer = Observer(w);
        observer.RequestPermission = requestPermission;
        return w.Context(AccessWorld.TenantA, observer);
    }

    private static async Task GiveAsync(AccessWorld w, Guid employee, string template, Guid? branch)
    {
        using var db = w.Context(AccessWorld.TenantA);
        var role = db.AccessRoles.Single(r => r.TemplateKey == template);
        db.RoleAssignments.Add(new RoleAssignment
        {
            TenantId = AccessWorld.TenantA, EmployeeId = employee, AccessRoleId = role.Id,
            Scope = branch is null ? AccessScopeType.Company : AccessScopeType.Branch,
            LocationId = branch, CreatedByEmployeeId = w.Admin1,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<ClaimsPrincipal> ThroughMiddlewareAsync(AccessWorld w, Guid caller, string role, string permission)
    {
        var db = Decided(w);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", caller.ToString()), new Claim("role", role)], "test")),
        };
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new PermissionAttribute(permission)), "test"));
        ClaimsPrincipal? seen = null;
        var middleware = new AuthzEnforcedMiddleware(ctx => { seen = ctx.User; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, db);
        Assert.Equal(permission, db.AuthzObserver!.RequestPermission);
        return seen!;
    }

    [Fact]
    public async Task The_request_role_is_the_models_in_an_enforced_company_and_the_tokens_anywhere_else()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await GiveAsync(w, w.E4, AccessRoleTemplates.Observer, null);

        await SetModeAsync(w, AuthzMode.Legacy);
        Assert.Equal("Employee", (await ThroughMiddlewareAsync(w, w.E2, "Employee", AccessPermissions.EmployeesView)).FindFirstValue("role"));
        Assert.Equal("Manager", (await ThroughMiddlewareAsync(w, w.M1, "Manager", AccessPermissions.EmployeesView)).FindFirstValue("role"));

        await SetModeAsync(w, AuthzMode.Enforced);
        var timekeeper = await ThroughMiddlewareAsync(w, w.E2, "Employee", AccessPermissions.EmployeesView);
        Assert.Equal("Manager", timekeeper.FindFirstValue("role"));      // the narrowed shape
        Assert.Equal("Employee", timekeeper.FindFirstValue(AuthzEnforcedMiddleware.TokenRoleClaim));
        Assert.Equal(w.E2.ToString(), timekeeper.FindFirstValue("sub"));
        // A right the role does not carry: plain staff for this request (the gate refuses it anyway).
        Assert.Equal("Employee", (await ThroughMiddlewareAsync(w, w.E2, "Employee", AccessPermissions.PayrollView)).FindFirstValue("role"));
        // A company-wide observer: every branch, but never the admin's shape.
        Assert.Equal("Manager", (await ThroughMiddlewareAsync(w, w.E4, "Employee", AccessPermissions.EmployeesView)).FindFirstValue("role"));
        // Today's two roles read as themselves.
        Assert.Equal("Admin", (await ThroughMiddlewareAsync(w, w.Admin1, "Admin", AccessPermissions.EmployeesView)).FindFirstValue("role"));
        Assert.Equal("Manager", (await ThroughMiddlewareAsync(w, w.M1, "Manager", AccessPermissions.EmployeesView)).FindFirstValue("role"));
    }

    // ─────────────────────────── the scope rules, with the request's own right ───────────────────────────

    private delegate Task<bool> Rule(AppDbContext db, Guid caller, EmployeeRole role, Guid target, CancellationToken ct);

    private static readonly (string Name, Rule Ask)[] RecordRules =
    [
        ("CanAccess", (db, c, r, t, ct) => LocationScopeRules.CanAccessEmployeeAsync(db, c, r, t, ct)),
        ("CanManage", LocationScopeRules.CanManageEmployeeAsync),
    ];

    private static readonly string?[] RequestRights =
    [
        null, AccessPermissions.DevicesApprove, AccessPermissions.DevicesRevoke, AccessPermissions.EmployeesResetPin,
        AccessPermissions.AttendanceFraud, AccessPermissions.FieldVisitsManage, AccessPermissions.FieldVisitsView,
        AccessPermissions.AttendancePhotosView, AccessPermissions.AttendanceView, AccessPermissions.LeavesManage,
    ];

    [Fact]
    public async Task Todays_admins_and_managers_get_todays_answers_whatever_right_the_request_names()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();

        async Task<Dictionary<string, bool>> AnswersAsync(AuthzMode mode)
        {
            await SetModeAsync(w, mode);
            var answers = new Dictionary<string, bool>();
            foreach (var right in RequestRights)
                foreach (var (name, ask) in RecordRules)
                    foreach (var caller in w.Panel)
                        foreach (var target in w.Everyone)
                        {
                            using var db = Decided(w, right);
                            answers[$"{name}[{right ?? "-"}] {w.Name(caller)}→{w.Name(target)}"] =
                                await ask(db, caller, w.RoleOf(caller), target, default);
                        }
            return answers;
        }

        var today = await AnswersAsync(AuthzMode.Legacy);
        var enforced = await AnswersAsync(AuthzMode.Enforced);
        var differ = today.Where(kv => enforced[kv.Key] != kv.Value).Select(kv => $"{kv.Key}: bu gün {kv.Value}").ToList();
        Assert.True(differ.Count == 0, string.Join("\n", differ));
        Assert.True(today.Count > 1500, $"only {today.Count} answers compared");
    }

    [Fact]
    public async Task A_list_narrows_to_where_the_request_right_is_held_not_only_where_people_are_seen()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E2: timekeeper of B1, observer of B2 — sees people at both, approves phones at B1 only.
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await GiveAsync(w, w.E2, AccessRoleTemplates.Observer, w.B2);
        await SetModeAsync(w, AuthzMode.Enforced);

        using (var db = Decided(w, AccessPermissions.EmployeesView))
            Assert.Equal(new[] { w.B1, w.B2 }.Order(), (await LocationScopeRules.ManagedLocationIdsAsync(db, w.E2, default)).Order());
        using (var db = Decided(w, AccessPermissions.DevicesApprove))
            Assert.Equal(new[] { w.B1 }, await LocationScopeRules.ManagedLocationIdsAsync(db, w.E2, default));
        using (var db = Decided(w, AccessPermissions.PayrollView))
            Assert.Empty(await LocationScopeRules.ManagedLocationIdsAsync(db, w.E2, default));
        // Acting on a record follows the same right: a phone at B1 yes, at B2 no.
        using (var db = Decided(w, AccessPermissions.DevicesApprove))
        {
            Assert.True(await LocationScopeRules.CanManageEmployeeAsync(db, w.E2, EmployeeRole.Manager, w.E1, default));
            Assert.False(await LocationScopeRules.CanManageEmployeeAsync(db, w.E2, EmployeeRole.Manager, w.Operator, default));
        }
    }

    [Fact]
    public async Task A_timekeeper_sees_their_peoples_days_but_not_their_selfies()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        using var db = Decided(w);
        Assert.True(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E2, EmployeeRole.Employee, w.E1, default,
            AccessPermissions.AttendanceView));
        Assert.False(await LocationScopeRules.CanAccessEmployeeAsync(db, w.E2, EmployeeRole.Employee, w.E1, default,
            AccessPermissions.AttendancePhotosView));
        // Today's manager of B1 sees both, as today.
        Assert.True(await LocationScopeRules.CanAccessEmployeeAsync(db, w.M1, EmployeeRole.Manager, w.E1, default,
            AccessPermissions.AttendancePhotosView));
    }

    [Fact]
    public async Task One_timekeeper_cannot_answer_another_timekeepers_pin_request()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        // E1 works at B1 and keeps its time; E2 keeps B1's time too. Colleagues, not one above the other.
        await GiveAsync(w, w.E1, AccessRoleTemplates.Timekeeper, w.B1);
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        using var db = Decided(w, AccessPermissions.EmployeesResetPin);
        // The records rule alone would let them (a colleague's records are reachable)…
        Assert.True(await LocationScopeRules.CanManageEmployeeAsync(db, w.E2, EmployeeRole.Manager, w.E1, default));
        // …the account rule does not: that is the one the queue now also asks.
        Assert.False(await LocationScopeRules.EnforcedCredentialTargetAsync(db, w.E2, EmployeeRole.Manager, w.E1, default));
        // B1's legacy manager, from above, still may.
        Assert.True(await LocationScopeRules.EnforcedCredentialTargetAsync(db, w.M1, EmployeeRole.Manager, w.E1, default));

        // Legacy: the queue's own checks decide, unchanged.
        await SetModeAsync(w, AuthzMode.Legacy);
        using var legacy = Decided(w, AccessPermissions.EmployeesResetPin);
        Assert.True(await LocationScopeRules.EnforcedCredentialTargetAsync(legacy, w.E2, EmployeeRole.Employee, w.E1, default));
    }

    // ─────────────────────────── the manager desk's rows ───────────────────────────

    private static ManagerController Desk(AccessWorld w, AppDbContext db, Guid caller) =>
        new(db, new Hasher(), new NoSummary(), w.Options)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    // The role the middleware leaves on the request for a non-admin holder.
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", "Manager")], "test")),
                },
            },
        };

    private static Dictionary<Guid, (bool Manageable, string? Phone)> Rows(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return ((System.Collections.IEnumerable)ok.Value!).Cast<object>().ToDictionary(
            o => (Guid)o.GetType().GetProperty("id")!.GetValue(o)!,
            o => ((bool)o.GetType().GetProperty("manageable")!.GetValue(o)!,
                  (string?)o.GetType().GetProperty("phoneNumber")!.GetValue(o)));
    }

    [Fact]
    public async Task On_the_staff_list_an_observer_acts_on_nobody_and_reads_no_numbers_and_a_timekeeper_reaches_their_own()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        await GiveAsync(w, w.E3, AccessRoleTemplates.Observer, w.B1);
        await SetModeAsync(w, AuthzMode.Enforced);

        var observer = Rows(await Desk(w, Decided(w, AccessPermissions.EmployeesView), w.E3).Employees());
        Assert.Contains(w.E1, observer.Keys);
        Assert.All(observer.Values, r => Assert.False(r.Manageable));
        Assert.All(observer.Values, r => Assert.Null(r.Phone));

        var timekeeper = Rows(await Desk(w, Decided(w, AccessPermissions.EmployeesView), w.E2).Employees());
        Assert.True(timekeeper[w.E1].Manageable);
        Assert.NotNull(timekeeper[w.E1].Phone);
        // B1's admin and its managers clock in there: seen, never acted on, no numbers.
        Assert.False(timekeeper[w.Admin1].Manageable);
        Assert.Null(timekeeper[w.Admin1].Phone);
        Assert.False(timekeeper[w.M1].Manageable);
        Assert.Null(timekeeper[w.M1].Phone);
        // Nobody from another branch.
        Assert.All(timekeeper.Keys, k => Assert.Equal(w.B1, w.HomeOf(k)));

        // Today's manager of B1 and B2 keeps today's rows.
        await SetModeAsync(w, AuthzMode.Legacy);
        var legacy = Rows(await Desk(w, Decided(w, AccessPermissions.EmployeesView), w.M1).Employees());
        await SetModeAsync(w, AuthzMode.Enforced);
        var enforced = Rows(await Desk(w, Decided(w, AccessPermissions.EmployeesView), w.M1).Employees());
        // One honest difference: today's row flag calls the platform operator's row (plain staff at B2) editable,
        // and the save then refuses it (CannotManageOperator). The model's flag says so up front.
        Assert.True(legacy[w.Operator].Manageable);
        Assert.False(enforced[w.Operator].Manageable);
        Assert.Equal(legacy.Where(kv => kv.Key != w.Operator).OrderBy(kv => kv.Key),
            enforced.Where(kv => kv.Key != w.Operator).OrderBy(kv => kv.Key));
    }

    // ─────────────────────────── the bell ───────────────────────────

    private static AdminNotificationsController Bell(AccessWorld w, AppDbContext db, Guid caller, string role) =>
        new(new DeviceChangeService(db, new DeviceBindingOptions()), new ReportQueryService(db, new AppOptions { TimeZone = "UTC" }),
            db, new AppOptions { TimeZone = "UTC" })
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("sub", caller.ToString()), new Claim("role", role)], "test")),
                },
            },
        };

    private static int Count(IActionResult result, string property)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return (int)ok.Value!.GetType().GetProperty(property)!.GetValue(ok.Value)!;
    }

    [Fact]
    public async Task A_timekeepers_bell_counts_their_branch_and_the_admins_counts_the_company()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);
        using (var db = w.Context(AccessWorld.TenantA))
        {
            foreach (var person in new[] { w.E1, w.E3, w.E4 })
            {
                db.PinResetRequests.Add(new PinResetRequest { TenantId = AccessWorld.TenantA, EmployeeId = person });
                db.DeviceChangeRequests.Add(new DeviceChangeRequest
                {
                    TenantId = AccessWorld.TenantA, EmployeeId = person, NewDeviceFingerprint = $"fp-{person:N}",
                });
            }
            await db.SaveChangesAsync();
        }
        await SetModeAsync(w, AuthzMode.Enforced);

        var mine = await Bell(w, Decided(w, AccessPermissions.DashboardView), w.E2, "Manager").Badges();
        Assert.Equal(1, Count(mine, "pinResets"));        // E1, at B1
        Assert.Equal(1, Count(mine, "deviceChanges"));
        Assert.Equal(0, Count(mine, "overdueTasks"));     // no «tasks.use»

        var all = await Bell(w, Decided(w, AccessPermissions.DashboardView), w.Admin1, "Admin").Badges();
        Assert.Equal(3, Count(all, "pinResets"));
        Assert.Equal(3, Count(all, "deviceChanges"));
    }

    // ─────────────────────────── staff alerts ───────────────────────────

    [Fact]
    public async Task A_staff_alert_reaches_whoever_holds_alerts_over_the_branch_and_can_act_on_it()
    {
        using var w = new AccessWorld();
        await w.ReconcileAndLoadAsync();
        await GiveAsync(w, w.E2, AccessRoleTemplates.Timekeeper, w.B1);   // alerts + phones + PINs at B1
        await GiveAsync(w, w.E3, AccessRoleTemplates.Observer, w.B1);     // no alerts
        await SetModeAsync(w, AuthzMode.Enforced);

        async Task<(List<Guid> Local, List<Guid> All)> For(string kind, Guid subject)
        {
            using var db = Decided(w);
            return await StaffAlertDispatcher.ModelRecipientsAsync(db,
                new StaffAlert(AccessWorld.TenantA, subject, kind, DateTime.UtcNow), w.HomeOf(subject));
        }

        var gps = await For("GpsPermissionDenied", w.E1);
        Assert.Contains(w.E2, gps.Local);                 // the timekeeper standing there
        Assert.Contains(w.M1, gps.Local);                 // B1's managers, as today
        Assert.DoesNotContain(w.E3, gps.All);             // an observer is not alerted
        Assert.DoesNotContain(w.Admin1, gps.Local);       // the company-wide hear it only when nobody local does
        Assert.Equal(gps.Local, gps.All);

        var pin = await For(StaffAlertKinds.PinResetRequested, w.E1);
        Assert.Contains(w.E2, pin.Local);

        // A branch nobody runs: the company-wide holders hear it — the admins, as today.
        var b6 = Guid.NewGuid();
        var e6 = Guid.NewGuid();
        using (var db = w.Context(AccessWorld.TenantA))
        {
            db.Locations.Add(new Location { Id = b6, TenantId = AccessWorld.TenantA, Name = "B6", IsActive = true, WorkDaysMask = 127, QrVersion = 1 });
            db.Employees.Add(new Employee
            {
                Id = e6, TenantId = AccessWorld.TenantA, FullName = "E6", Email = $"{e6:N}@a.local", Role = EmployeeRole.Employee,
                LocationId = b6, IsActive = true, PasswordHash = "hashed:1234", ActivatedAtUtc = DateTime.UtcNow.AddDays(-30),
            });
            await db.SaveChangesAsync();
        }
        var nobodyLocal = await For("GpsPermissionDenied", e6);
        Assert.Empty(nobodyLocal.Local);
        Assert.Equal(new[] { w.Admin1, w.Admin2 }.Order(), nobodyLocal.All.Order());
    }

    // ─────────────────────────── stubs ───────────────────────────

    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string hash, string password) => hash == "hashed:" + password;
    }

    private sealed class NoSummary : IDailySummaryService
    {
        public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default) => Task.FromResult(0);
    }
}
