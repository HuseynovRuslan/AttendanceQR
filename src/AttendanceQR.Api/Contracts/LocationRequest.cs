using System.ComponentModel.DataAnnotations;

namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Create/update payload for a Location. Shift times are "HH:mm" strings (what the browser's
/// &lt;input type="time"&gt; emits) and are parsed server-side into <see cref="TimeOnly"/>.
/// </summary>
/// <param name="Latitude">Bounds-checked for the same reason as ScanRequest.Latitude: a non-finite
/// coordinate poisons the geofence comparison into always passing. Admin-supplied rather than
/// attacker-supplied, but it is the same geofence and the same silent failure. Bounds are doubles
/// (-90d) deliberately — see ScanRequest.</param>
/// <param name="RadiusMeters">A non-positive radius would admit nobody; the geofence is meaningless
/// without it. Upper bound keeps a typo from covering the country.</param>
public record LocationRequest(
    string Name,
    [property: Range(-90d, 90d)] double Latitude,
    [property: Range(-180d, 180d)] double Longitude,
    [property: Range(1, 100_000)] int RadiusMeters,
    string ShiftStart,
    string ShiftEnd,
    int LateThresholdMinutes,
    int WorkDaysMask);

/// <summary>Enable/disable a location without deleting it.</summary>
public record SetActiveRequest(bool IsActive);
