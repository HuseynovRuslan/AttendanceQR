using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>The «PIN-i unutdum» queue's own bookkeeping, outside the screens that work it.</summary>
public static class PinResetQueue
{
    /// <summary>
    /// The employee has just signed in with a PIN that worked, so anything they asked for is moot: close
    /// it as <see cref="PinResetStatus.Recovered"/>. Called from both logins.
    ///
    /// IgnoreQueryFilters because app-login has no tenant to filter on; EmployeeId is a global key, so it
    /// reaches exactly this person's rows. Tracked rather than ExecuteUpdate — it is at most a row or two,
    /// and it runs through the same save as everything else.
    /// </summary>
    public static async Task CloseOnSignInAsync(AppDbContext db, Guid employeeId, DateTime nowUtc, CancellationToken ct = default)
    {
        var open = await db.PinResetRequests.IgnoreQueryFilters()
            .Where(r => r.EmployeeId == employeeId && r.Status == PinResetStatus.Pending)
            .ToListAsync(ct);
        if (open.Count == 0)
            return;

        foreach (var request in open)
        {
            request.Status = PinResetStatus.Recovered;
            request.ResolvedAtUtc = nowUtc;
        }
        await db.SaveChangesAsync(ct);
    }
}
