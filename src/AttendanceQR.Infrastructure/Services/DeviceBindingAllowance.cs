using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// How much of an employee's automatic-adoption allowance is spent. The scan path refuses on this
/// count and the device-change screen explains it, so both read it from here: an admin deciding on a
/// request is shown the same number the poster acted on, not a second opinion of it.
/// </summary>
public static class DeviceBindingAllowance
{
    /// <summary>
    /// Automatic adoptions inside the current window — see <see cref="DeviceBindingRules.AutoBindWindowStart"/>
    /// for why an approval starts it again.
    /// </summary>
    public static async Task<int> UsedAsync(AppDbContext db, Guid employeeId, DateTime nowUtc, CancellationToken ct = default)
    {
        var lastApproval = await db.DeviceChangeRequests
            .Where(r => r.EmployeeId == employeeId && r.Status == DeviceChangeStatus.Approved)
            .MaxAsync(r => r.ReviewedAtUtc, ct);

        var since = DeviceBindingRules.AutoBindWindowStart(nowUtc, lastApproval);
        return await db.AuditLogs.CountAsync(a =>
            a.EmployeeId == employeeId
            && a.EventType == AuditEventType.DeviceAutoBound
            && a.CreatedAtUtc >= since, ct);
    }

    /// <summary>
    /// Every new device the employee arrived with in the last thirty days, adopted at a poster or
    /// approved by an admin. Three in a month is rarely three phones — it is one phone whose browser
    /// keeps forgetting the app, and the fix is a conversation, not another approval.
    /// </summary>
    public static Task<int> RecentNewDevicesAsync(AppDbContext db, Guid employeeId, DateTime nowUtc, CancellationToken ct = default)
    {
        var since = nowUtc.AddDays(-DeviceBindingRules.AutoBindWindowDays);
        return db.AuditLogs.CountAsync(a =>
            a.EmployeeId == employeeId
            && (a.EventType == AuditEventType.DeviceAutoBound || a.EventType == AuditEventType.DeviceChangeApproved)
            && a.CreatedAtUtc >= since, ct);
    }
}
