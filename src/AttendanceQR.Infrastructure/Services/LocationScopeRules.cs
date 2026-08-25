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
    /// Whether <paramref name="requesterId"/> may SEE <paramref name="targetEmployeeId"/>'s data
    /// (attendance drill-down, check-in selfies, missed-checkout detail).
    ///
    /// Seeing is wider than managing, and the two came apart on purpose. A manager's boards used to
    /// stop at Role==Employee, which meant a site with two managers showed each of them a headcount
    /// that was short by the other — a branch of 97 read as 95, and no screen explained the gap. That
    /// is not a safeguard, it is a wrong number: they work the same site and see each other every
    /// morning.
    ///
    /// What the Role==Employee rule was actually protecting is ACTING on somebody — resetting a PIN,
    /// editing an account, closing their day — because a manager who could act on a same-branch ADMIN
    /// could take the company (the P0 of 2026-08-08). That rule is untouched and lives in
    /// <see cref="CanManageEmployeeAsync"/>. This one only decides who is visible, and the answer is
    /// everyone at the branches they manage.
    /// </summary>
    public static async Task<bool> CanAccessEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        // Everyone can always see their own records.
        if (requesterId == targetEmployeeId)
            return true;

        if (role == EmployeeRole.Admin)
            return true;
        if (role != EmployeeRole.Manager)
            return false;

        // Anyone at a branch this manager oversees, whatever their role. Note this includes an ADMIN
        // who clocks in at that branch: a manager can see that they arrived, and their check-in
        // selfie, exactly as for anyone else standing at the same gate. They still cannot touch the
        // account — see CanManageEmployeeAsync, which is what reset-pin, edit and delete ask.
        var location = await db.Employees
            .Where(e => e.Id == targetEmployeeId)
            .Select(e => (Guid?)e.LocationId)
            .FirstOrDefaultAsync(ct);
        if (location is not Guid branch)
            return false;

        var managed = await ManagedLocationIdsAsync(db, requesterId, ct);
        return managed.Contains(branch);
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
