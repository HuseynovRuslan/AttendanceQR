using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api;

/// <summary>
/// The audit row for a change to somebody's role — Employee, Manager or Admin — or to the branches a manager
/// runs, which is the same thing seen from the permission model: those branches ARE where the manager's
/// rights reach.
///
/// Until this existed nobody could tell who had made somebody an admin. The employee form changed
/// <see cref="Employee.Role"/> in place, AccessSeeder then mirrored it as a system row with no author, and
/// the only trace left was the time that row was made.
///
/// One row per change, against the person whose role changed (<see cref="AuditLog.EmployeeId"/>), in their
/// company (<see cref="AuditLog.TenantId"/>), at <see cref="AuditLog.CreatedAtUtc"/>. Reason holds the rest as
/// JSON: who made the change (and the operator behind a support session), the role and the access it gave
/// before and after, and the endpoint and screen it came through.
///
/// <para><b>Same transaction.</b> <see cref="Add"/> only adds the row to the caller's context; the caller's
/// own SaveChanges writes it together with the change, so there is never a role change without its row nor
/// a row for a change that failed.</para>
///
/// <para><b>Never a secret.</b> Only ids, role and template names, counts, branch ids and the route. No PIN, hash,
/// token, phone number or e-mail: the builder is never handed one, so it cannot write one.</para>
/// </summary>
public static class RoleChangeAudit
{
    /// <summary>Where a role can change. The route, and where a person presses it.</summary>
    public static class Via
    {
        public const string AdminEmployeeEdit =
            "PUT /api/admin/employees/{id} · Admin paneli › İşçilər › İşçini redaktə et";
        public const string AdminInvite =
            "POST /api/admin/employees/invite · Admin paneli › İşçilər › Yeni işçi";
        public const string AdminBulkInvite =
            "POST /api/admin/employees/bulk-invite · Admin paneli › Toplu əlavə (dəvət linki)";
        public const string AdminBulkImport =
            "POST /api/admin/employees/bulk-import · Admin paneli › Toplu əlavə (müvəqqəti PIN)";
        public const string OperatorTenantCreate =
            "POST /api/super/tenants · Operator konsolu › Yeni şirkət";
        public const string OperatorTenantAdmin =
            "POST /api/super/tenants/{id}/admin · Operator konsolu › Admin təyin et";
    }

    /// <summary>What happened to the account: its role or branches changed, it was created holding a role
    /// above Employee, or an unclaimed admin account was handed to a person.</summary>
    public static class Actions
    {
        public const string Changed = "changed";
        public const string Created = "created";
        public const string Claimed = "claimed";
    }

    /// <summary>One permission-model role the account holds because of its Employee.Role — the same rows
    /// AccessSeeder mirrors it into. The three are locked templates defined by the code, so the key names their
    /// permissions exactly; the count is written beside it, and a manager's grant carries how many branches it
    /// reaches. Which branches changed is <see cref="Entry.BranchesAdded"/> / <see cref="Entry.BranchesRemoved"/>.</summary>
    public sealed record Grant(string Template, string Scope, int PermissionCount, int? BranchCount);

    /// <summary>The account's role and the access it gives. A null role means there was no account yet.</summary>
    public sealed record Side(string? Role, IReadOnlyList<Grant> Grants);

    public sealed record Entry(
        int V, string Action, Guid Tenant, Guid Target, Guid? By, Guid? Operator, string Via, Side From, Side To,
        IReadOnlyList<Guid>? BranchesAdded, IReadOnlyList<Guid>? BranchesRemoved, bool BranchesOmitted);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// The longest Reason the column takes (AuditLogConfiguration: 1000). A row that does not fit fails the WHOLE
    /// save it rides in — the role change with it — so the row is built to fit, with the limit read from the model
    /// so a later change of the column is followed here. The first version wrote every permission and branch in
    /// full and so refused every role change on Postgres; the in-memory test database does not enforce lengths,
    /// which is why the tests now check it themselves.
    /// </summary>
    public static int MaxReasonLength(AppDbContext db) =>
        db.Model.FindEntityType(typeof(AuditLog))?.FindProperty(nameof(AuditLog.Reason))?.GetMaxLength() ?? 1000;

    /// <summary>
    /// The access an Employee.Role gives, as AccessSeeder writes it: an Admin holds Company administrator over
    /// the company; a Manager holds Branch manager (legacy) at each managed branch and PIN helper over the company;
    /// an Employee holds nothing beyond their own record.
    /// </summary>
    public static Side Describe(EmployeeRole? role, IEnumerable<Guid> branches) => role switch
    {
        null => new Side(null, []),
        EmployeeRole.Admin => new Side(nameof(EmployeeRole.Admin),
        [
            new Grant(AccessRoleTemplates.CompanyAdmin, "company",
                AccessRoleTemplates.CompanyAdminTemplate.Permissions.Distinct().Count(), null),
        ]),
        EmployeeRole.Manager => new Side(nameof(EmployeeRole.Manager),
        [
            new Grant(AccessRoleTemplates.LegacyManager, "branch",
                AccessRoleTemplates.LegacyManagerTemplate.Permissions.Distinct().Count(), branches.Distinct().Count()),
            new Grant(AccessRoleTemplates.PinHelper, "company",
                AccessRoleTemplates.PinHelperTemplate.Permissions.Distinct().Count(), null),
        ]),
        _ => new Side(nameof(EmployeeRole.Employee), []),
    };

    /// <summary>A role changed, or a manager's branches did. An Admin's or Employee's branch rows mean nothing
    /// (ApplyManagedLocations removes them), so a difference there is not a change of access.</summary>
    public static bool Changed(EmployeeRole fromRole, IEnumerable<Guid> fromBranches, EmployeeRole toRole, IEnumerable<Guid> toBranches)
        => fromRole != toRole
           || (toRole == EmployeeRole.Manager && !fromBranches.ToHashSet().SetEquals(toBranches));

    /// <summary>
    /// The branches <paramref name="employeeId"/> will manage once the context is saved: the rows the context
    /// tracks for them that are not being deleted. Valid after ApplyManagedLocationsAsync, which loads every
    /// existing row with tracking before it adds or removes any.
    /// </summary>
    public static List<Guid> PendingBranches(AppDbContext db, Guid employeeId) =>
        db.ChangeTracker.Entries<ManagedLocation>()
            .Where(e => e.Entity.EmployeeId == employeeId && e.State is not (EntityState.Deleted or EntityState.Detached))
            .Select(e => e.Entity.LocationId)
            .Distinct()
            .ToList();

    /// <summary>
    /// Adds — does not save — the audit row for one change to <paramref name="db"/>. The caller's SaveChanges
    /// commits it with the change.
    /// </summary>
    public static AuditLog Add(
        AppDbContext db, ClaimsPrincipal user, string? ip, Guid tenantId, Guid targetId, string action,
        EmployeeRole? fromRole, IEnumerable<Guid> fromBranches, EmployeeRole toRole, IEnumerable<Guid> toBranches,
        string via)
    {
        // A branch counts only while the person is a manager: an Admin or Employee holds no branch grant.
        var heldBefore = fromRole == EmployeeRole.Manager ? fromBranches.Distinct().ToList() : [];
        var heldAfter = toRole == EmployeeRole.Manager ? toBranches.Distinct().ToList() : [];

        var row = new AuditLog
        {
            TenantId = tenantId,
            EmployeeId = targetId,
            EventType = AuditEventType.EmployeeRoleChanged,
            Reason = Serialize(MaxReasonLength(db), action, tenantId, targetId, ActorOf(user), OperatorBehind(user), via,
                Describe(fromRole, heldBefore), Describe(toRole, heldAfter),
                heldAfter.Except(heldBefore).Order().ToList(), heldBefore.Except(heldAfter).Order().ToList()),
            IpAddress = ip,
        };
        db.AuditLogs.Add(row);
        return row;
    }

    /// <summary>
    /// The row as JSON, shortened until it fits <paramref name="max"/>: first the branch ids go (their counts stay, and
    /// BranchesOmitted says so), then the screen half of «via» (the route stays). Who, whom, the roles and the grants
    /// are always kept — a few hundred characters at most.
    /// </summary>
    public static string Serialize(
        int max, string action, Guid tenant, Guid target, Guid? by, Guid? op, string via, Side from, Side to,
        IReadOnlyList<Guid> added, IReadOnlyList<Guid> removed)
    {
        var full = JsonSerializer.Serialize(
            new Entry(2, action, tenant, target, by, op, via, from, to, added, removed, false), Json);
        if (full.Length <= max)
            return full;

        var withoutBranches = JsonSerializer.Serialize(
            new Entry(2, action, tenant, target, by, op, via, from, to, null, null, true), Json);
        if (withoutBranches.Length <= max)
            return withoutBranches;

        var route = via.Split(" · ")[0];
        return JsonSerializer.Serialize(
            new Entry(2, action, tenant, target, by, op, route, from, to, null, null, true), Json);
    }

    /// <summary><see cref="Add"/> when <see cref="Changed"/> says something did; otherwise nothing.</summary>
    public static AuditLog? AddIfChanged(
        AppDbContext db, ClaimsPrincipal user, string? ip, Guid tenantId, Guid targetId,
        EmployeeRole fromRole, IReadOnlyCollection<Guid> fromBranches, EmployeeRole toRole, IReadOnlyCollection<Guid> toBranches,
        string via)
        => Changed(fromRole, fromBranches, toRole, toBranches)
            ? Add(db, user, ip, tenantId, targetId, Actions.Changed, fromRole, fromBranches, toRole, toBranches, via)
            : null;

    /// <summary>The account that made the change: the signed-in one. In a support session that is the company
    /// admin being borrowed — the operator behind it is recorded separately.</summary>
    private static Guid? ActorOf(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;

    /// <summary>The operator behind a support session (the token's "imp" claim), or null outside one.</summary>
    private static Guid? OperatorBehind(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue("imp"), out var id) ? id : null;
}
