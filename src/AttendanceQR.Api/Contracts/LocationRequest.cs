namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Create/update payload for a Location. Shift times are "HH:mm" strings (what the browser's
/// &lt;input type="time"&gt; emits) and are parsed server-side into <see cref="TimeOnly"/>.
/// </summary>
public record LocationRequest(
    string Name,
    double Latitude,
    double Longitude,
    int RadiusMeters,
    string ShiftStart,
    string ShiftEnd,
    int LateThresholdMinutes);

/// <summary>Enable/disable a location without deleting it.</summary>
public record SetActiveRequest(bool IsActive);
