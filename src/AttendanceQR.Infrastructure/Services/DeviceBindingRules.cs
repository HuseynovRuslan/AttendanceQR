using AttendanceQR.Domain.Entities;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// The one place that decides which of an employee's device bindings survive. Shared by the scan
/// path (auto-bind) and the admin device-change approval, so the two can never drift apart.
/// </summary>
public static class DeviceBindingRules
{
    /// <summary>
    /// Makes <paramref name="fingerprint"/> an active binding, evicting the least-recently-used
    /// bindings so at most <paramref name="maxActive"/> remain. A fingerprint that was bound before
    /// and evicted is reactivated rather than inserted again — the (EmployeeId, DeviceFingerprint)
    /// unique index means a second row for it would fail.
    /// </summary>
    /// <returns>
    /// The binding to use. It is a NEW entity when this fingerprint was never seen — the caller must
    /// add it to the context (check with <c>existing.Contains(result)</c>). Nothing is saved here.
    /// </returns>
    public static DeviceBinding Bind(
        IReadOnlyCollection<DeviceBinding> existing,
        Guid employeeId,
        string fingerprint,
        string? label,
        int maxActive,
        DateTime nowUtc)
    {
        var match = existing.FirstOrDefault(d =>
            string.Equals(d.DeviceFingerprint, fingerprint, StringComparison.Ordinal));

        // The incoming binding claims a slot whether it is new or being reactivated, so it is
        // excluded here and the eviction is sized as if it were already in.
        var active = existing
            .Where(d => d.IsActive && d != match)
            .OrderBy(d => d.LastSeenAtUtc)
            .ToList();

        // Clamped: a misconfigured maxActive of 0 or less must not walk off the end of the list.
        var evictCount = Math.Clamp(active.Count + 1 - maxActive, 0, active.Count);
        for (var i = 0; i < evictCount; i++)
            active[i].IsActive = false;

        if (match is not null)
        {
            match.IsActive = true;
            match.BoundAtUtc = nowUtc;
            match.LastSeenAtUtc = nowUtc;
            if (label is not null) match.DeviceLabel = label;
            return match;
        }

        return new DeviceBinding
        {
            EmployeeId = employeeId,
            DeviceFingerprint = fingerprint,
            DeviceLabel = label,
            BoundAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            IsActive = true
        };
    }
}
