namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// How permissive device binding is, bound from the "DeviceBinding" section. Set via environment
/// variables (<c>DeviceBinding__AutoBind</c>) so the rules can be tightened without a redeploy.
/// </summary>
public sealed class DeviceBindingOptions
{
    public const string SectionName = "DeviceBinding";

    /// <summary>
    /// When true, an unrecognised device scanning from INSIDE the geofence is adopted silently
    /// instead of being rejected with DeviceMismatch. Open during the rollout, when employees are
    /// moving between Safari and the installed PWA; every adoption is still audited and revocable.
    /// Turn off to restore the old "one device, admin approves changes" behaviour.
    /// </summary>
    public bool AutoBind { get; set; } = true;

    /// <summary>Bindings kept per employee; the least recently used is evicted beyond this.</summary>
    public int MaxActiveDevices { get; set; } = 3;

    /// <summary>
    /// Guards against private browsing, where every session is a fresh storage context and would
    /// otherwise mint a binding on every single scan, for ever. A normal employee never gets near it.
    /// </summary>
    public int MaxBindsPer30Days { get; set; } = 3;

    /// <summary>
    /// How many DIFFERENT employees one device may carry.
    ///
    /// Every other limit here is per EMPLOYEE, so nothing bounded the other axis: one phone could
    /// accumulate the whole company. The client caps its saved profiles at 60, but that lives in
    /// localStorage and is therefore not a control at all.
    ///
    /// The number is an operational ceiling rather than a security one — a shared phone past about
    /// fifteen people is already unworkable, because each scan takes half a minute of queue and the
    /// brigade spends a quarter of an hour twice a day standing at a poster.
    /// </summary>
    public int MaxAccountsPerDevice { get; set; } = 20;
}
