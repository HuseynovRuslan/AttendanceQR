using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// A push subscription belongs to the session that registered it. A TokenVersion bump — a PIN reset,
/// «hər yerdən çıxış», a deactivation through the admin edit, a PIN change — ends that session on every
/// device, and the device's pushes end with it. Signing in again on the device re-registers it (the app
/// does that once per token, see frontend/src/lib/push.ts), and the pushes come back.
///
/// Decided HERE, at send time, rather than at each of the places that bump TokenVersion: there are
/// fifteen of them, and the sixteenth would forget. Fail-closed — a row whose version is not the
/// employee's current one is never sent to, whatever path changed the version.
/// </summary>
public static class PushSessionBinding
{
    /// <summary>
    /// The employees' subscriptions that may be sent to. Rows of an ended session are deleted in the same
    /// pass (they can never become live again: the version only goes up, and signing in re-registers the
    /// device as a fresh binding). Rows from before the binding existed (<see cref="PushSubscription.Unbound"/>)
    /// are adopted to the current version — the startup adoption normally got there first.
    /// Runs inside the caller's tenant scope; the join is filtered like every other query.
    /// </summary>
    public static async Task<List<PushSubscription>> LiveSubscriptionsAsync(
        AppDbContext db, IReadOnlyCollection<Guid> employeeIds, CancellationToken ct = default)
    {
        var rows = await db.PushSubscriptions
            .Where(s => employeeIds.Contains(s.EmployeeId))
            .Join(db.Employees, s => s.EmployeeId, e => e.Id, (s, e) => new { Sub = s, Current = e.TokenVersion })
            .ToListAsync(ct);

        var live = new List<PushSubscription>(rows.Count);
        var ended = new List<PushSubscription>();
        var adopted = false;
        foreach (var row in rows)
        {
            if (row.Sub.TokenVersion < 0)
            {
                row.Sub.TokenVersion = row.Current;
                adopted = true;
                live.Add(row.Sub);
            }
            else if (row.Sub.TokenVersion == row.Current)
                live.Add(row.Sub);
            else
                ended.Add(row.Sub);
        }

        if (ended.Count > 0 || adopted)
        {
            db.PushSubscriptions.RemoveRange(ended);
            await db.SaveChangesAsync(ct);
        }
        return live;
    }

    /// <summary>
    /// Stamps every pre-binding row of the current tenant with its employee's current TokenVersion.
    /// Idempotent: a second run finds nothing. Returns how many rows it adopted.
    /// </summary>
    public static async Task<int> AdoptUnboundAsync(AppDbContext db, CancellationToken ct = default)
    {
        var rows = await db.PushSubscriptions
            .Where(s => s.TokenVersion < 0)
            .Join(db.Employees, s => s.EmployeeId, e => e.Id, (s, e) => new { Sub = s, Current = e.TokenVersion })
            .ToListAsync(ct);
        if (rows.Count == 0) return 0;

        foreach (var row in rows)
            row.Sub.TokenVersion = row.Current;
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
