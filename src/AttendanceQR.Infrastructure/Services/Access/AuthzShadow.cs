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
/// </summary>
public interface IAuthzObserver
{
    Task ObserveScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        CancellationToken ct = default);

    Task ObserveManagedSetAsync(
        AppDbContext db, Guid callerId, EmployeeRole callerRole, IReadOnlyCollection<Guid> legacy,
        CancellationToken ct = default);
}

public static class ShadowRules
{
    public const string CanAccess = "CanAccess";
    public const string CanSchedule = "CanSchedule";
    public const string CanManage = "CanManage";
    public const string CredentialTarget = "CredentialTarget";
    public const string ManageableEmployee = "ManageableEmployee";
    public const string LeaveSubject = "LeaveSubject";
    public const string ManagedSet = "ManagedSet";

    /// <summary>
    /// The permission model's answer to the question each of today's rules asks. One table, so the shadow,
    /// the equivalence tests and — at Phase 5 — the enforcing code all mean the same thing by each rule.
    /// </summary>
    public static bool Decide(
        string rule, EffectiveAccess actor, Guid targetId, Guid targetHome, EffectiveAccess target, bool targetIsOperator) =>
        rule switch
        {
            // See a person's records.
            CanAccess => AccessRules.CanSee(actor, AccessPermissions.EmployeesView, targetId, targetHome),
            // Roster a person (shift overrides) — today a manager may roster themselves even off their branches.
            CanSchedule => actor.Has(AccessPermissions.SchedulesAssign)
                           && (targetId == actor.EmployeeId || actor.Covers(AccessPermissions.SchedulesAssign, targetHome)),
            // Act on a person's records (attendance, devices, requests). Operators are not refused here — today's
            // rule does not refuse them either; the credential paths below do.
            CanManage => AccessRules.CanActOn(actor, AccessPermissions.AttendanceCorrect, targetId, targetHome, target,
                targetIsOperator, refuseOperators: false, refuseSelf: false),
            // Hand a person a new PIN or login number.
            CredentialTarget => AccessRules.CanActOn(actor, AccessPermissions.EmployeesResetPin, targetId, targetHome, target,
                targetIsOperator, refuseOperators: true, refuseSelf: true),
            // Edit a person's profile from the manager desk.
            ManageableEmployee => AccessRules.CanActOn(actor, AccessPermissions.EmployeesUpdate, targetId, targetHome, target,
                targetIsOperator, refuseOperators: true, refuseSelf: true),
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

    public async Task ObserveScopeAsync(
        AppDbContext db, string rule, Guid callerId, EmployeeRole callerRole, Guid targetId, bool legacy,
        CancellationToken ct = default)
    {
        try
        {
            if (await ModeAsync(db, _cache, ct) != AuthzMode.Shadow)
                return;
            var graph = _graph ??= await AccessGraph.LoadAsync(db, ct);
            if (!_homes.TryGetValue(targetId, out var home))
            {
                var row = await db.Employees.AsNoTracking().Where(e => e.Id == targetId).Select(e => (Guid?)e.LocationId).FirstOrDefaultAsync(ct);
                _homes[targetId] = home = (row ?? Guid.Empty, row is not null);
            }
            if (!home.Found)
                return; // today's rule answered for a person who is not there; nothing to compare

            var decided = ShadowRules.Decide(rule, graph.For(callerId), targetId, home.Home, graph.For(targetId), _operatorIds.Contains(targetId));
            if (decided != legacy)
                await _sink.RecordAsync(db, new ShadowRecord(db.CurrentTenantId, rule, $"{rule} as {callerRole}", callerId, targetId.ToString(), legacy, decided), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Authz shadow: {Rule} could not be compared", rule);
        }
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
            var graph = _graph ??= await AccessGraph.LoadAsync(db, ct);
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
