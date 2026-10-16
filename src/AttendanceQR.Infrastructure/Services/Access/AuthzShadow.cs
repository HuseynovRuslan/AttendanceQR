using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AttendanceQR.Infrastructure.Services.Access;

/// <summary>The platform operators (App:SuperAdminEmployeeIds), read once at start-up. Their rows are never a
/// target of the credential rules.</summary>
public sealed record OperatorAllowlist(IReadOnlySet<Guid> Ids);

/// <summary>One disagreement, before it is counted.</summary>
public sealed record ShadowRecord(
    Guid TenantId, string Kind, string Subject, Guid EmployeeId, string TargetKey, bool Legacy, bool New);

/// <summary>Where disagreements go. The production sink upserts a per-day counter; tests capture them.</summary>
public interface IAuthzShadowSink
{
    Task RecordAsync(AppDbContext db, ShadowRecord record, CancellationToken ct);
}

/// <summary>
/// Hears every scope decision today's rules make (LocationScopeRules and the manager desk's own helpers) and,
/// for a company in Shadow mode, makes the same decision with the permission model. Today's answer is the
/// one that is used; a different answer is recorded. Never throws and never changes a decision — a failure
/// here costs one row of evidence, never a request.
///
/// Since Phase 5 it also DECIDES, for a company switched to Enforced: the Decide*/Scope methods return the
/// permission model's answer there, and today's everywhere else (Legacy and Shadow are untouched). In Enforced
/// an answer that cannot be computed is «no» — a permission check that fails open is not a permission check.
/// </summary>
public interface IAuthzObserver
{
    Task ObserveScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        CancellationToken ct = default);

    Task ObserveManagedSetAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, IReadOnlyCollection<Guid> legacy,
        CancellationToken ct = default);

    /// <summary>The company's answer to one of today's rules: the model's in Enforced, today's otherwise (Shadow
    /// records a difference, as before).</summary>
    Task<bool> DecideScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        CancellationToken ct = default);

    /// <summary>The branches a manager-shaped list is narrowed to: in Enforced, where the caller holds
    /// «employees.view» (every branch for company-wide); otherwise today's ManagedLocations.</summary>
    Task<IReadOnlyCollection<Guid>> DecideManagedSetAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, IReadOnlyCollection<Guid> legacy,
        CancellationToken ct = default);

    /// <summary>Where the caller may use <paramref name="permission"/>: the whole company, some branches, or
    /// nowhere. Today's answer (Admin — everywhere; Manager — their ManagedLocations; else nowhere) in Legacy
    /// and Shadow, the model's in Enforced.</summary>
    Task<ScopeAnswer> ScopeAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, string permission, CancellationToken ct = default);

    /// <summary>The company's mode, as the deciding methods read it.</summary>
    Task<AuthzMode> ModeAsync(AppDbContext db, CancellationToken ct = default);

    /// <summary>
    /// The right this request stands for — the endpoint's [Permission] (AuthzEnforcedMiddleware), or a report's
    /// own (LocationScopeRules.EffectiveRoleAsync). A list narrowed to «my branches» and «may I act on this
    /// person» then ask for it as well as for «employees.view»: a timekeeper who sees two branches but approves
    /// phones at one approves at that one. Null: the rules' own permissions, as in Phase 3.
    /// </summary>
    string? RequestPermission { get; set; }

    /// <summary>The role today's code paths take for this caller (AccessProjection.RoleFor) in a company switched
    /// to Enforced; their own role anywhere else. Unreadable in Enforced → plain staff.</summary>
    Task<EmployeeRole> ProjectRoleAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, string permission, CancellationToken ct = default);

    /// <summary>The company's live assignments, loaded once per request.</summary>
    Task<AccessGraph> GraphAsync(AppDbContext db, CancellationToken ct = default);

    /// <summary>The platform operators — never a target of the account rules.</summary>
    IReadOnlySet<Guid> OperatorIds { get; }

    /// <summary><see cref="DecideScopeAsync(AppDbContext, string, Guid, EmployeeRole, Guid, bool, CancellationToken)"/>,
    /// naming what the records are where the request does not (LocationScopeRules.CanAccessEmployeeAsync).</summary>
    Task<bool> DecideScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        string? permission, CancellationToken ct = default);
}

/// <summary>Where one permission reaches for one caller.</summary>
public sealed record ScopeAnswer(bool Company, IReadOnlySet<Guid> Branches)
{
    public static ScopeAnswer Nowhere { get; } = new(false, new HashSet<Guid>());
    public static ScopeAnswer Everywhere { get; } = new(true, new HashSet<Guid>());

    public bool Any => Company || Branches.Count > 0;
    public bool Covers(Guid locationId) => Company || Branches.Contains(locationId);
}

public static class ShadowRules
{
    public const string CanAccess = "CanAccess";
    public const string CanSchedule = "CanSchedule";
    public const string CanManage = "CanManage";
    public const string CredentialTarget = "CredentialTarget";
    public const string ManageableEmployee = "ManageableEmployee";
    public const string LeaveSubject = "LeaveSubject";
    public const string CorrectAttendance = "CorrectAttendance";
    public const string ManagedSet = "ManagedSet";

    /// <summary>
    /// The permission model's answer to the question each of today's rules asks. One table, so the shadow,
    /// the equivalence tests and — at Phase 5 — the enforcing code all mean the same thing by each rule.
    /// <paramref name="actorHome"/> is the caller's own branch: the strict account rule needs it to read
    /// today's colleague rule in the reverse direction.
    /// </summary>
    /// <param name="permission">The right the request stands for (IAuthzObserver.RequestPermission), read by the two
    /// rules about a person's RECORDS: seeing them needs that right at the person's branch as well as
    /// «employees.view» (a timekeeper sees their people's days, not their selfies), and acting on them needs that
    /// right — approving a phone is «devices.approve», not «attendance.correct». Null — the rule's own, as before.
    /// Today's admins and managers hold every right of their role over the same branches, so for them the answer
    /// is the same either way (AuthzEnforcedTests).</param>
    public static bool Decide(
        string rule, EffectiveAccess actor, Guid targetId, Guid targetHome, EffectiveAccess target, bool targetIsOperator,
        Guid? actorHome = null, string? permission = null) =>
        rule switch
        {
            // See a person's records.
            CanAccess => AccessRules.CanSee(actor, AccessPermissions.EmployeesView, targetId, targetHome)
                         && (permission is null || AccessRules.CanSee(actor, permission, targetId, targetHome)),
            // Roster a person (shift overrides) — today a manager may roster themselves even off their branches.
            CanSchedule => actor.Has(AccessPermissions.SchedulesAssign)
                           && (targetId == actor.EmployeeId || actor.Covers(AccessPermissions.SchedulesAssign, targetHome)),
            // Act on a person's records (devices, requests). Operators are not refused here — today's rule does not
            // refuse them either; the credential paths below do. Records, not accounts: not strict.
            CanManage => permission is null
                ? AccessRules.CanActOn(actor, AccessPermissions.AttendanceCorrect, targetId, targetHome, target,
                    targetIsOperator, refuseOperators: false, refuseSelf: false)
                // The request's right, over somebody the actor can see: «employees.view» keeps a company-wide right
                // (today's PIN helper) from reaching people at branches the actor does not run.
                : AccessRules.CanActOn(actor, permission, targetId, targetHome, target,
                      targetIsOperator, refuseOperators: false, refuseSelf: false)
                  && AccessRules.CanActOn(actor, AccessPermissions.EmployeesView, targetId, targetHome, target,
                      targetIsOperator, refuseOperators: false, refuseSelf: false),
            // Correct a person's attendance — never one's own (the owner, 2026-10-15), whoever one is.
            CorrectAttendance => AccessRules.CanActOn(actor, AccessPermissions.AttendanceCorrect, targetId, targetHome, target,
                targetIsOperator, refuseOperators: false, refuseSelf: true),
            // Hand a person a new PIN or login number — an account action: only from above.
            CredentialTarget => AccessRules.CanActOn(actor, AccessPermissions.EmployeesResetPin, targetId, targetHome, target,
                targetIsOperator, refuseOperators: true, refuseSelf: true, strict: true, actorHome: actorHome),
            // Edit a person's profile from the manager desk — an account action: only from above.
            ManageableEmployee => AccessRules.CanActOn(actor, AccessPermissions.EmployeesUpdate, targetId, targetHome, target,
                targetIsOperator, refuseOperators: true, refuseSelf: true, strict: true, actorHome: actorHome),
            // File leave for a person — today admins at the branch included, operators never.
            LeaveSubject => !targetIsOperator && AccessRules.CanSee(actor, AccessPermissions.LeavesManage, targetId, targetHome),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
        };
}

public sealed class AuthzObserver : IAuthzObserver
{
    private static readonly TimeSpan ModeCacheFor = TimeSpan.FromSeconds(30);

    private readonly IAuthzShadowSink _sink;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthzObserver> _logger;
    private readonly IReadOnlySet<Guid> _operatorIds;
    private AccessGraph? _graph;
    private readonly Dictionary<Guid, (Guid Home, bool Found)> _homes = new();

    public string? RequestPermission { get; set; }

    public IReadOnlySet<Guid> OperatorIds => _operatorIds;

    /// <summary>The permission Decide reads for <paramref name="rule"/>: an explicit one, else the request's —
    /// only for the two rules about records (see ShadowRules.Decide).</summary>
    private string? PermissionFor(string rule, string? permission) =>
        rule is ShadowRules.CanAccess or ShadowRules.CanManage ? permission ?? RequestPermission : null;

    public async Task<AccessGraph> GraphAsync(AppDbContext db, CancellationToken ct = default) =>
        _graph ??= await AccessGraph.LoadAsync(db, ct);

    public async Task<EmployeeRole> ProjectRoleAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, string permission, CancellationToken ct = default)
    {
        if (await ModeAsync(db, _cache, ct) != AuthzMode.Enforced)
            return callerRole;
        try
        {
            return AccessProjection.RoleFor((await GraphAsync(db, ct)).For(callerId), permission);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Authz enforced: role for {Permission} could not be read — plain staff", permission);
            return EmployeeRole.Employee;
        }
    }

    public AuthzObserver(IAuthzShadowSink sink, IMemoryCache cache, ILogger<AuthzObserver> logger, OperatorAllowlist operators)
    {
        _sink = sink;
        _cache = cache;
        _logger = logger;
        _operatorIds = operators.Ids;
    }

    /// <summary>Is this company in Shadow mode? Cached for 30 s — it is asked on every panel request.</summary>
    public static async Task<AuthzMode> ModeAsync(AppDbContext db, IMemoryCache cache, CancellationToken ct)
    {
        var tenantId = db.CurrentTenantId;
        var key = $"authz-mode:{tenantId:N}";
        if (cache.TryGetValue(key, out AuthzMode mode))
            return mode;
        mode = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.AuthzMode).FirstOrDefaultAsync(ct);
        cache.Set(key, mode, ModeCacheFor);
        return mode;
    }

    public Task ObserveScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        CancellationToken ct = default) =>
        ObserveScopeAsync(db, rule, callerId, callerRole, targetId, legacy, null, ct);

    private async Task ObserveScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        string? permission, CancellationToken ct)
    {
        try
        {
            if (await ModeAsync(db, _cache, ct) != AuthzMode.Shadow)
                return;
            var graph = await GraphAsync(db, ct);
            var home = await HomeOfAsync(db, targetId, ct);
            if (!home.Found)
                return; // today's rule answered for a person who is not there; nothing to compare
            var callerHome = await HomeOfAsync(db, callerId, ct);

            var decided = ShadowRules.Decide(rule, graph.For(callerId), targetId, home.Home, graph.For(targetId),
                _operatorIds.Contains(targetId), callerHome.Found ? callerHome.Home : null, PermissionFor(rule, permission));
            if (decided != legacy)
                await _sink.RecordAsync(db, new ShadowRecord(db.CurrentTenantId, rule, $"{rule} as {callerRole}", callerId, targetId.ToString(), legacy, decided), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Authz shadow: {Rule} could not be compared", rule);
        }
    }

    public Task<AuthzMode> ModeAsync(AppDbContext db, CancellationToken ct = default) => ModeAsync(db, _cache, ct);

    public Task<bool> DecideScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        CancellationToken ct = default) =>
        DecideScopeAsync(db, rule, callerId, callerRole, targetId, legacy, null, ct);

    public async Task<bool> DecideScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        string? permission, CancellationToken ct = default)
    {
        if (await ModeAsync(db, _cache, ct) != AuthzMode.Enforced)
        {
            await ObserveScopeAsync(db, rule, callerId, callerRole, targetId, legacy, permission, ct);
            return legacy;
        }
        try
        {
            var graph = await GraphAsync(db, ct);
            var home = await HomeOfAsync(db, targetId, ct);
            if (!home.Found)
                return false;
            var callerHome = await HomeOfAsync(db, callerId, ct);
            return ShadowRules.Decide(rule, graph.For(callerId), targetId, home.Home, graph.For(targetId),
                _operatorIds.Contains(targetId), callerHome.Found ? callerHome.Home : null, PermissionFor(rule, permission));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Authz enforced: {Rule} could not be decided — refused", rule);
            return false;
        }
    }

    public async Task<IReadOnlyCollection<Guid>> DecideManagedSetAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, IReadOnlyCollection<Guid> legacy,
        CancellationToken ct = default)
    {
        if (await ModeAsync(db, _cache, ct) != AuthzMode.Enforced)
        {
            await ObserveManagedSetAsync(db, callerId, callerRole, legacy, ct);
            return legacy;
        }
        // Where the caller sees people, and — where the request names its own right — holds that right too.
        var seen = await ScopeAsync(db, callerId, callerRole, AccessPermissions.EmployeesView, ct);
        var used = RequestPermission is { } p && p != AccessPermissions.EmployeesView
            ? await ScopeAsync(db, callerId, callerRole, p, ct)
            : seen;
        if (seen.Company && used.Company)
            return await db.Locations.AsNoTracking().Select(l => l.Id).ToListAsync(ct);
        if (seen.Company)
            return used.Branches.ToList();
        return used.Company ? seen.Branches.ToList() : seen.Branches.Intersect(used.Branches).ToList();
    }

    public async Task<ScopeAnswer> ScopeAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, string permission, CancellationToken ct = default)
    {
        if (await ModeAsync(db, _cache, ct) != AuthzMode.Enforced)
            return callerRole switch
            {
                EmployeeRole.Admin => ScopeAnswer.Everywhere,
                EmployeeRole.Manager => new ScopeAnswer(false, (await db.ManagedLocations.AsNoTracking()
                    .Where(m => m.EmployeeId == callerId).Select(m => m.LocationId).ToListAsync(ct)).ToHashSet()),
                _ => ScopeAnswer.Nowhere,
            };
        try
        {
            var access = (await GraphAsync(db, ct)).For(callerId);
            if (access.HasAtCompany(permission))
                return ScopeAnswer.Everywhere;
            var branches = access.BranchesFor(permission);
            return branches is null ? ScopeAnswer.Everywhere : new ScopeAnswer(false, branches.ToHashSet());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Authz enforced: scope of {Permission} could not be read — nowhere", permission);
            return ScopeAnswer.Nowhere;
        }
    }

    /// <summary>A person's home branch, asked once per request per person.</summary>
    private async Task<(Guid Home, bool Found)> HomeOfAsync(AppDbContext db, Guid employeeId, CancellationToken ct)
    {
        if (_homes.TryGetValue(employeeId, out var home))
            return home;
        var row = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => (Guid?)e.LocationId).FirstOrDefaultAsync(ct);
        return _homes[employeeId] = (row ?? Guid.Empty, row is not null);
    }

    public async Task ObserveManagedSetAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, IReadOnlyCollection<Guid> legacy,
        CancellationToken ct = default)
    {
        try
        {
            // Today only a manager's set means anything; every caller asks for the Manager role first.
            if (callerRole != EmployeeRole.Manager || await ModeAsync(db, _cache, ct) != AuthzMode.Shadow)
                return;
            var graph = await GraphAsync(db, ct);
            var caller = graph.For(callerId);
            if (caller.HasAtCompany(AccessPermissions.EmployeesView))
                return;
            var branches = caller.BranchesFor(AccessPermissions.EmployeesView);
            var same = branches is not null && branches.SetEquals(legacy);
            if (!same)
                await _sink.RecordAsync(db, new ShadowRecord(db.CurrentTenantId, ShadowRules.ManagedSet, "ManagedLocations",
                    callerId, $"legacy {legacy.Count} / new {(branches is null ? "company" : branches.Count.ToString())}", true, false), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Authz shadow: managed set could not be compared");
        }
    }
}

/// <summary>
/// Upserts one row per (company, day, kind, subject, person, target) and counts repeats — never a 23505, never
/// the request's change tracker (raw SQL, so a controller's pending edits are not saved early by accident).
/// On a non-relational store (the in-memory tests) it does nothing.
/// </summary>
public sealed class DbShadowSink : IAuthzShadowSink
{
    private readonly ILogger<DbShadowSink> _logger;

    public DbShadowSink(ILogger<DbShadowSink> logger) => _logger = logger;

    public async Task RecordAsync(AppDbContext db, ShadowRecord r, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
            return;
        var now = DateTime.UtcNow;
        var day = DateOnly.FromDateTime(now);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AuthzShadowEvents"
                    ("Id", "TenantId", "Day", "Kind", "Subject", "EmployeeId", "TargetKey", "Legacy", "New", "Count", "FirstAtUtc", "LastAtUtc")
                VALUES ({Guid.NewGuid()}, {r.TenantId}, {day}, {Trim(r.Kind, 40)}, {Trim(r.Subject, 300)}, {r.EmployeeId},
                        {Trim(r.TargetKey, 300)}, {r.Legacy}, {r.New}, 1, {now}, {now})
                ON CONFLICT ("TenantId", "Day", "Kind", "Subject", "EmployeeId", "TargetKey")
                DO UPDATE SET "Count" = "AuthzShadowEvents"."Count" + 1, "LastAtUtc" = EXCLUDED."LastAtUtc",
                              "Legacy" = EXCLUDED."Legacy", "New" = EXCLUDED."New"
                """, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Authz shadow: could not record {Kind} {Subject}", r.Kind, r.Subject);
        }
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
