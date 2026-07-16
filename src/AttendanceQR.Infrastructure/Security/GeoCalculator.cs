namespace AttendanceQR.Infrastructure.Security;

public static class GeoCalculator
{
    private const double EarthRadiusMeters = 6_371_000d;

    /// <summary>
    /// Great-circle distance between two coordinates using the Haversine formula, in metres.
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate is not finite.</exception>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        // Backstop for the geofence. Haversine maps a non-finite input to NaN, and callers ask
        // `distance > radius` — which is FALSE for NaN, so a poisoned coordinate reads as "inside"
        // and check-in is accepted from anywhere. Requests are bounds-checked at the contract, so
        // this should be unreachable; throw rather than hand back a value that silently disables the
        // check if some future caller skips validation.
        if (!double.IsFinite(lat1) || !double.IsFinite(lon1) || !double.IsFinite(lat2) || !double.IsFinite(lon2))
            throw new ArgumentException(
                $"Coordinates must be finite (got {lat1},{lon1} → {lat2},{lon2}).");

        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
