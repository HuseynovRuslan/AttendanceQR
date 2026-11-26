using System.ComponentModel.DataAnnotations;

namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Create/update payload for a Location. Shift times are "HH:mm" strings (what the browser's
/// &lt;input type="time"&gt; emits) and are parsed server-side into <see cref="TimeOnly"/>.
/// </summary>
/// <param name="Latitude">Bounds-checked for the same reason as ScanRequest.Latitude: a non-finite
/// coordinate poisons the geofence comparison into always passing. Admin-supplied rather than
/// attacker-supplied, but it is the same geofence and the same silent failure. No [property:], and
/// doubles not ints — see ScanRequest, both rules are load-bearing.</param>
/// <param name="RadiusMeters">A non-positive radius would admit nobody; the geofence is meaningless
/// without it. Upper bound keeps a typo from covering the country.</param>
public record LocationRequest(
    string Name,
    [Range(-90d, 90d)] double Latitude,
    [Range(-180d, 180d)] double Longitude,
    [Range(1, 100_000)] int RadiusMeters,
    string ShiftStart,
    string ShiftEnd,
    int LateThresholdMinutes,
    int WorkDaysMask,
    // Nullable, and null means "leave it as it is" — NOT false. An admin editing a radius from a
    // tab that loaded before this field existed posts no value for it, and a default of false would
    // switch a branch's whole check-in method off with no log and no screen saying so: the next
    // morning fourteen phones open a QR camera at a branch that has no poster. Same trap as
    // EmployeeUpdateRequest, not repeated.
    bool? QrlessCheckIn = null,
    // Nullable for the same reason as the field above: an update that does not mention the fence
    // must never be read as "take the fence down".
    bool? RequireGeofence = null,
    // «Ştat» — how many people this branch is staffed for; the daily report's vacancy figure is
    // ştat minus the people actually on its list. Null = leave as it is (same trap as above),
    // 0 = clear it. Checked in TryValidate, not with an attribute (a [property: Range] on a record
    // 500s every request that binds it).
    int? Headcount = null,
    // «Mövcudluq yoxlaması» for everybody at the branch, every N hours (Location.PresenceCheckEveryHours).
    // Null = leave as it is (same trap as above), 0 = off, otherwise one of PresenceCheckRules.AllowedEveryHours.
    // Admin only; checked in TryValidate and the controller, not with an attribute.
    int? PresenceCheckEveryHours = null);

/// <summary>Enable/disable a location without deleting it.</summary>
public record SetActiveRequest(bool IsActive);
