namespace AttendanceQR.Domain.Enums;

/// <summary>How a device binding came to exist — the first thing an admin wants to know when
/// reviewing the list before tightening the rules.</summary>
public enum DeviceBindingOrigin
{
    /// <summary>Bound when the employee activated their account.</summary>
    Activation = 0,

    /// <summary>Adopted automatically: an unknown device scanned from inside the geofence.</summary>
    AutoBind = 1,

    /// <summary>An admin approved a device-change request.</summary>
    AdminApproval = 2
}
