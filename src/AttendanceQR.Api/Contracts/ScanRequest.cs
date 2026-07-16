using System.ComponentModel.DataAnnotations;

namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Check-in/out scan payload. The employee identity now comes from the authenticated JWT
/// (the "sub" claim), not from the body.
/// </summary>
/// <param name="Latitude">
/// Where the phone says it is. Range-checked because the geofence compares <c>distance > radius</c>,
/// and a non-finite coordinate makes that comparison FALSE rather than true: JSON "1e400" parses to
/// double.PositiveInfinity, Haversine turns that into NaN, and every comparison against NaN is false
/// — so an unchecked coordinate is accepted from anywhere on earth. The bounds reject NaN too (it
/// compares false against both ends). Attributes are [property:] so they land on the property, which
/// is what MVC validates — on a positional record they would otherwise bind to the constructor
/// parameter and silently never run. The bounds are written as doubles (-90d, not -90) on purpose:
/// integer literals pick RangeAttribute's int overload, which validates via Convert.ToInt32 and
/// throws OverflowException on infinity — a 500 instead of a 400.
/// </param>
/// <param name="PhotoBase64">
/// Optional check-in selfie for photo audit — a WebP data URL (or bare base64) captured silently by
/// the client. Optional by design: if the camera is unavailable it is omitted and check-in proceeds
/// without a photo. Only sent for check-in, never check-out.
/// </param>
public record ScanRequest(
    string QrToken,
    string DeviceFingerprint,
    [property: Range(-90d, 90d)] double Latitude,
    [property: Range(-180d, 180d)] double Longitude,
    string? PhotoBase64 = null);
