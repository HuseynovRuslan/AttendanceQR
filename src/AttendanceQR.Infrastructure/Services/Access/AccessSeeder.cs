using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AttendanceQR.Infrastructure.Services.Access;

/// <summary>
/// Keeps the permission model in step with today's access while both exist
/// (docs/strukturlar-ve-selahiyyetler.md §4).
///
/// - Every company has the six template roles; the three locked ones are re-synced from the code.
/// - Today's access is mirrored as SYSTEM assignments (CreatedByEmployeeId null): an Admin holds Company
///   administrator company-wide; a Manager holds Branch manager (legacy) at each of their ManagedLocations
///   and PIN helper company-wide; nobody else holds a legacy role.
/// - Assignments a PERSON made are never touched here — and one that already grants what the system would
///   add stands in for the system's row instead of colliding with it.
///
/// Run on every start (so drift — a hand-run UPDATE, a path that changes a role without telling anyone —
/// heals itself) and right after the writes that change Employee.Role or ManagedLocations. Idempotent:
/// a second run changes nothing.
/// </summary>
public static class AccessSeeder
{
    private static readonly string[] LegacyKeys =
        [AccessRoleTemplates.CompanyAdmin, AccessRoleTemplates.LegacyManager, AccessRoleTemplates.PinHelper];

    /// <summary>
    /// Every company. Returns the number of rows changed. A company that fails is logged and skipped, its
    /// unsaved rows dropped: the companies share this context, and one bad row (staging, 2026-10-15: a
    /// ManagedLocations row pointing at another company's branch) used to throw out of the loop and leave
    /// every company after it unreconciled — on every start.
    /// </summary>
    public static async Task<int> ReconcileAllAsync(AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(ct);
        var changed = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                changed += await ReconcileTenantAsync(db, tenantId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ForgetPending(db);
                logger?.LogWarning(ex, "Access reconcile failed for tenant {TenantId}; the other companies go on", tenantId);
            }
        }
        return changed;
    }

    /// <summary>One company. Explicit TenantId and IgnoreQueryFilters, so it runs the same at start-up (no
    /// tenant resolved) and inside a request.</summary>
    public static async Task<int> ReconcileTenantAsync(AppDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var changed = 0;
        var roles = await EnsureTemplatesAsync(db, tenantId, ct);
        changed += db.ChangeTracker.Entries<AccessRole>().Count(e => e.State is EntityState.Added or EntityState.Modified);

        var adminRole = roles[AccessRoleTemplates.CompanyAdmin].Id;
        var managerRole = roles[AccessRoleTemplates.LegacyManager].Id;
        var pinRole = roles[AccessRoleTemplates.PinHelper].Id;

        var people = await db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.Role != EmployeeRole.Employee)
            .Select(e => new { e.Id, e.Role })
            .ToListAsync(ct);
        var managerIds = people.Where(p => p.Role == EmployeeRole.Manager).Select(p => p.Id).ToHashSet();
        var managed = await db.ManagedLocations.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .Select(m => new { m.EmployeeId, m.LocationId })
            .ToListAsync(ct);

        var desired = new HashSet<(Guid Employee, Guid Role, AccessScopeType Scope, Guid? Location)>();
        foreach (var person in people)
        {
            if (person.Role == EmployeeRole.Admin)
                desired.Add((person.Id, adminRole, AccessScopeType.Company, null));
            else if (person.Role == EmployeeRole.Manager)
                desired.Add((person.Id, pinRole, AccessScopeType.Company, null));
        }
        // Only a manager's rows mean anything today (every scope check asks for the Manager role first).
        foreach (var m in managed.Where(m => managerIds.Contains(m.EmployeeId)))
            desired.Add((m.EmployeeId, managerRole, AccessScopeType.Branch, m.LocationId));

        var legacyRoleIds = new[] { adminRole, managerRole, pinRole };
        var live = await db.RoleAssignments.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.RevokedAtUtc == null && legacyRoleIds.Contains(a.AccessRoleId))
            .ToListAsync(ct);

        // A row a PERSON made for a key the system would add already grants it, so it stands in for the
        // system's row. Adding the system's anyway is a second active row for one key — which the unique index
        // refuses (on Postgres; the in-memory tests never would), and the refusal took this company's whole
        // reconcile down, on every start: a manager made admin by hand, later made Admin on the employee form.
        // Nothing of the person's is revoked here; if they take it away, the next run adds the system's own.
        foreach (var held in live.Where(a => a.CreatedByEmployeeId != null))
            desired.Remove((held.EmployeeId, held.AccessRoleId, held.Scope, held.LocationId));

        var now = DateTime.UtcNow;
        foreach (var assignment in live.Where(a => a.CreatedByEmployeeId == null))
        {
            // Remove, not Contains: a second identical row is then no longer desired, so it is revoked too.
            if (!desired.Remove((assignment.EmployeeId, assignment.AccessRoleId, assignment.Scope, assignment.LocationId)))
            {
                assignment.RevokedAtUtc = now;
                changed++;
            }
        }
        foreach (var (employee, role, scope, location) in desired)
        {
            db.RoleAssignments.Add(new RoleAssignment
            {
                TenantId = tenantId, EmployeeId = employee, AccessRoleId = role, Scope = scope, LocationId = location,
            });
            changed++;
        }

        if (changed > 0)
            await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>The company's template roles by key, created where missing; the locked ones brought back in
    /// line with the code. Not saved here.</summary>
    public static async Task<Dictionary<string, AccessRole>> EnsureTemplatesAsync(
        AppDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.AccessRoles.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.TemplateKey != null)
            .ToListAsync(ct);
        var byKey = existing.ToDictionary(r => r.TemplateKey!);

        foreach (var template in AccessRoleTemplates.All)
        {
            if (!byKey.TryGetValue(template.Key, out var role))
            {
                role = new AccessRole
                {
                    TenantId = tenantId, TemplateKey = template.Key, Name = template.Name,
                    Description = template.Description, IsLocked = template.IsLocked,
                    PeersManageable = template.PeersManageable, Permissions = template.Permissions.ToList(),
                };
                db.AccessRoles.Add(role);
                byKey[template.Key] = role;
                continue;
            }

            if (!template.IsLocked)
                continue;
            var permissionsDiffer = !role.Permissions.ToHashSet().SetEquals(template.Permissions);
            if (permissionsDiffer || role.Name != template.Name || role.Description != template.Description
                || !role.IsLocked || role.PeersManageable != template.PeersManageable || role.ArchivedAtUtc != null)
            {
                role.Name = template.Name;
                role.Description = template.Description;
                role.IsLocked = true;
                role.PeersManageable = template.PeersManageable;
                role.Permissions = template.Permissions.ToList();
                role.ArchivedAtUtc = null;
            }
        }
        return byKey;
    }

    /// <summary>
    /// Right after a write that changed somebody's role or a manager's branches — called once that write is
    /// SAVED — so the permission model is not a start-up behind. Never fails the write it follows; if it
    /// cannot run, it says so, drops what it left unsaved (or the request's next save would try the same rows
    /// again and fail in its place), and the next start-up reconcile does the same work.
    /// </summary>
    public static async Task ReconcileAfterWriteAsync(
        AppDbContext db, Guid tenantId, ILogger? logger, CancellationToken ct = default)
    {
        try
        {
            await ReconcileTenantAsync(db, tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ForgetPending(db);
            logger?.LogWarning(ex, "Access reconcile after write failed for tenant {TenantId}; the next start-up repeats it", tenantId);
        }
    }

    /// <summary>Detaches the roles and assignments a failed reconcile left unsaved. Only those: whatever else the
    /// context tracks belongs to the caller, and was saved before the reconcile ran.</summary>
    private static void ForgetPending(AppDbContext db)
    {
        foreach (var entry in db.ChangeTracker.Entries()
                     .Where(e => e.Entity is RoleAssignment or AccessRole
                                 && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
            entry.State = EntityState.Detached;
    }

    /// <summary>The system roles a reconcile manages — used by tests and the report.</summary>
    public static IReadOnlyList<string> LegacyTemplateKeys => LegacyKeys;
}
