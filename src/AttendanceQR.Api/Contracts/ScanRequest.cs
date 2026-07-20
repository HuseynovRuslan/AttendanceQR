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
/// compares false against both ends).
///
/// Two things about how these are written, both learned the hard way:
///   • NO [property:] prefix. On a positional record MVC validates the CONSTRUCTOR PARAMETER, and it
///     throws outright ("validation metadata must be associated with the constructor parameter")
///     if it finds the metadata on the property instead — every scan 500s. Note this is the opposite
///     of Validator.TryValidateObject, which reads properties; a test using that passes either way,
///     so it cannot tell you which one MVC wants.
///   • Bounds are doubles (-90d, not -90). Integer literals select RangeAttribute's int overload,
///     which validates via Convert.ToInt32 and throws OverflowException on infinity — a 500 again.
/// </param>
/// <param name="PhotoBase64">
/// Optional check-in selfie for photo audit — a WebP data URL (or bare base64) captured silently by
/// the client. Optional by design: if the camera is unavailable it is omitted and check-in proceeds
/// without a photo. Only sent for check-in, never check-out.
/// </param>
/// <param name="ClientScanId">
/// Client-generated id for this scan action (one per tap, reused across retries). Present on every
/// scan so a lost response or a re-sent offline queue item is de-duplicated by the server instead of
/// creating a second record. Null on older clients — then there is simply no idempotency.
/// </param>
/// <param name="ClientTimestampUtc">
/// The phone's own clock at the moment of the scan. Only used when <paramref name="Offline"/> is true
/// (an online scan is always stamped with server time). Trusted only within a sane window; outside it
/// the server falls back to its own time (see the Scan handler).
/// </param>
/// <param name="Offline">
/// True when this scan was captured with no connection and is now being synced. Switches the record's
/// time to the phone's clock and marks it WasOffline for the admin to audit.
/// </param>
public record ScanRequest(
    string QrToken,
    string DeviceFingerprint,
    [Range(-90d, 90d)] double Latitude,
    [Range(-180d, 180d)] double Longitude,
    string? PhotoBase64 = null,
    Guid? ClientScanId = null,
    DateTime? ClientTimestampUtc = null,
    bool Offline = false);
