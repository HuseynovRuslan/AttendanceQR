using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// The one place that decides which of an employee's device bindings survive. Shared by the scan
/// path (auto-bind) and the admin device-change approval, so the two can never drift apart.
/// </summary>
public static class DeviceBindingRules
{
    /// <summary>Why a handset may not be adopted for this employee. <c>null</c> means it may.</summary>
    public enum ShareRefusal
    {
        /// <summary>The employee's account may not ride on a device that is somebody else's.</summary>
        NotAllowed,

        /// <summary>The handset already carries as many accounts as it is permitted to.</summary>
        AccountLimit,
    }

    /// <summary>
    /// Whether an employee may be added to a device that ALREADY carries other people — the moment a
    /// phone becomes a brigade's shared handset.
    ///
    /// "One phone, one employee" is what stops a colleague clocking in for someone who did not turn
    /// up. Sharing gives that up for everybody on the device: the right trade for a worker who owns no
    /// phone, the wrong one as a capability every employee holds by default. Before this the default
    /// was exactly that — and nothing displayed it either, because every other limit in this file is
    /// per employee, which left the accounts-per-device axis unbounded and invisible.
    ///
    /// Asked only when adopting, never on an ordinary scan, so an arrangement already in use is not
    /// broken mid-morning — the person it would strand is standing at a poster unable to clock in.
    ///
    /// The permission is checked before the ceiling on purpose. Both refusals are actionable and the
    /// fixes are opposite — grant a permission, or split the brigade across two phones — so reporting
    /// the wrong one sends an admin down the wrong path.
    /// </summary>
    /// <param name="othersOnDevice">Distinct OTHER employees already actively bound to this handset.</param>
    public static ShareRefusal? MayJoinDevice(bool canShareDevice, int othersOnDevice, int maxAccountsPerDevice)
    {
        if (othersOnDevice <= 0) return null;              // their own device — nothing shared about it
        if (!canShareDevice) return ShareRefusal.NotAllowed;
        if (othersOnDevice + 1 > maxAccountsPerDevice) return ShareRefusal.AccountLimit;
        return null;
    }

    /// <summary>
    /// Makes <paramref name="fingerprint"/> an active binding, evicting the least-recently-used
    /// bindings so at most <paramref name="maxActive"/> remain. A fingerprint that was bound before
    /// and evicted is reactivated rather than inserted again — the (EmployeeId, DeviceFingerprint)
    /// unique index means a second row for it would fail.
    /// </summary>
    /// <remarks>
    /// Clears any revocation: calling this IS the authorization to trust the context again. Callers
    /// that are not an explicit human decision — i.e. auto-bind — must check <see
    /// cref="DeviceBinding.RevokedAtUtc"/> themselves and refuse before getting here.
    /// </remarks>
    /// <returns>
    /// The binding to use. It is a NEW entity when this fingerprint was never seen — the caller must
    /// add it to the context (check with <c>existing.Contains(result)</c>). Nothing is saved here.
    /// </returns>
    public static DeviceBinding Bind(
        IReadOnlyCollection<DeviceBinding> existing,
        Guid employeeId,
        string fingerprint,
        string? label,
        DeviceBindingOrigin origin,
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
            match.RevokedAtUtc = null;
            match.BoundVia = origin;
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
            BoundVia = origin,
            BoundAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            IsActive = true
        };
    }
}
