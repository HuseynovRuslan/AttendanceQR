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
    /// Whether <paramref name="requesterId"/> may see <paramref name="targetEmployeeId"/>'s data.
    /// Everyone sees their own; Admin sees all; a Manager sees anyone whose location is in their
    /// ManagedLocations; an Employee sees no one else.
    /// </summary>
    public static async Task<bool> CanAccessEmployeeAsync(
        AppDbContext db, Guid requesterId, EmployeeRole role, Guid targetEmployeeId, CancellationToken ct)
    {
        // Everyone can always see their own records.
        if (requesterId == targetEmployeeId)
            return true;

        if (role == EmployeeRole.Admin)
            return true;

        if (role == EmployeeRole.Manager)
        {
            var targetLocation = await db.Employees
                .Where(e => e.Id == targetEmployeeId)
                .Select(e => (Guid?)e.LocationId)
                .FirstOrDefaultAsync(ct);
            if (targetLocation is null)
                return false;

            var managed = await ManagedLocationIdsAsync(db, requesterId, ct);
            return managed.Contains(targetLocation.Value);
        }

        // Employee accessing anyone else.
        return false;
    }
}
