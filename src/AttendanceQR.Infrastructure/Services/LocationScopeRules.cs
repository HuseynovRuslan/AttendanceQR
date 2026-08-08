using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Single source of truth for location-based visibility (manager scope).
/// <para>
/// It lives in Infrastructure — not next to the Application-layer report scope — because
/// Infrastructure services (e.g. <see cref="AttendanceQueryService"/>) cannot reference Application,
/// whereas Application can reference Infrastructure. Putting the rule here lets BOTH the attendance
/// query (Infrastructure) and the report/export scope (Application) derive manager scope from one place.
/// A manager is scoped by their <c>ManagedLocations</c> set; their own <c>Employee.LocationId</c> is
/// never used for authorization.
/// </para>
/// </summary>
public static class LocationScopeRules
{
    /// <summary>The locations a manager oversees (their ManagedLocations set).</summary>
    public static Task<List<Guid>> ManagedLocationIdsAsync(AppDbContext db, Guid managerId, CancellationToken ct)
        => db.ManagedLocations
            .Where(m => m.EmployeeId == managerId)
            .Select(m => m.LocationId)
            .ToListAsync(ct);

    /// <summary>
    /// Whether <paramref name="requesterId"/> may see <paramref name="targetEmployeeId"/>'s data
    /// (attendance drill-down, check-in selfies, missed-checkout detail). Everyone sees their own;
    /// beyond self it is exactly <see cref="CanManageEmployeeAsync"/> — Admin sees all, a Manager sees
    /// only Role==Employee workers in their ManagedLocations, an Employee sees no one else. The
    /// manager branch once stopped at branch membership, which let a manager pull a same-branch
    /// admin's records and selfies; visibility now stops where management stops.
    /// </summary>
    public static async Task<bool> CanAccessEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        // Everyone can always see their own records — the one way this is wider than "manage".
        if (requesterId == targetEmployeeId)
            return true;

        return await CanManageEmployeeAsync(db, requesterId, role, targetEmployeeId, ct);
    }

    /// <summary>
    /// Whether <paramref name="requesterId"/> may MANAGE <paramref name="targetEmployeeId"/> — act on
    /// their account or records (assign/cancel field visits, pull their selfies, close their days).
    /// Stricter than <see cref="CanAccessEmployeeAsync"/> on two points, both learned the hard way:
    /// a Manager's reach stops at <c>Role == Employee</c> (an Admin or a fellow Manager also carries a
    /// LocationId — where they clock in — so branch membership alone would make the people ABOVE the
    /// manager valid targets), and self is NOT automatically included (managing means acting on another
    /// account; a manager's own visits go through the worker flow). Admin manages anyone in the tenant;
    /// tenant isolation itself comes from the global query filter on Employees. Fail-closed.
    /// </summary>
    public static async Task<bool> CanManageEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        if (role == EmployeeRole.Admin)
            return true;
        if (role != EmployeeRole.Manager)
            return false;

        var target = await db.Employees
            .Where(e => e.Id == targetEmployeeId)
            .Select(e => new { e.LocationId, e.Role })
            .FirstOrDefaultAsync(ct);
        if (target is null || target.Role != EmployeeRole.Employee)
            return false;

        var managed = await ManagedLocationIdsAsync(db, requesterId, ct);
        return managed.Contains(target.LocationId);
    }
}
