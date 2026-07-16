using AttendanceQR.Infrastructure.Security;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Covers <see cref="GeoCalculator.DistanceMeters"/> — the geofence gate. Every scan is accepted or
/// rejected on this number, so it is pinned against real Baku coordinates rather than round figures.
/// </summary>
public class GeoCalculatorTests
{
    // Two real points ~1.1 km apart in central Baku (İçərişəhər ↔ Fəvvarələr meydanı area).
    private const double Lat1 = 40.3660, Lon1 = 49.8352;
    private const double Lat2 = 40.3755, Lon2 = 49.8410;

    [Fact]
    public void Same_point_is_zero()
        => Assert.Equal(0, GeoCalculator.DistanceMeters(Lat1, Lon1, Lat1, Lon1), 6);

    [Fact]
    public void Known_baku_pair_is_about_1_1_km()
    {
        var d = GeoCalculator.DistanceMeters(Lat1, Lon1, Lat2, Lon2);
        Assert.InRange(d, 1_100, 1_200);
    }

    [Fact]
    public void Distance_is_symmetric()
    {
        var ab = GeoCalculator.DistanceMeters(Lat1, Lon1, Lat2, Lon2);
        var ba = GeoCalculator.DistanceMeters(Lat2, Lon2, Lat1, Lon1);
        Assert.Equal(ab, ba, 6);
    }

    [Fact]
    public void One_degree_of_latitude_is_about_111_km()
    {
        // Meridian arc — the one distance with a textbook value, so it catches a wrong Earth radius
        // or a degrees/radians mix-up.
        var d = GeoCalculator.DistanceMeters(40.0, 49.8, 41.0, 49.8);
        Assert.InRange(d, 111_000, 111_400);
    }

    [Fact]
    public void A_few_metres_apart_reads_as_a_few_metres()
    {
        // The precision that matters: a geofence radius is typically 50–150 m, so small offsets must
        // not round to zero. ~11 m of latitude (0.0001°).
        var d = GeoCalculator.DistanceMeters(Lat1, Lon1, Lat1 + 0.0001, Lon1);
        Assert.InRange(d, 10, 12);
    }

    [Fact]
    public void Longitude_degrees_shrink_with_latitude()
    {
        // At Baku's ~40°N a degree of longitude is cos(40°)≈0.77 of a degree of latitude. A formula
        // that forgot the cos(lat) term would make these equal.
        var lat = GeoCalculator.DistanceMeters(40.0, 49.8, 41.0, 49.8);
        var lon = GeoCalculator.DistanceMeters(40.0, 49.8, 40.0, 50.8);
        Assert.True(lon < lat * 0.8, $"expected longitude degree ({lon:F0} m) well under latitude degree ({lat:F0} m)");
    }

    // --- the open security finding (#2 in the audit) ---------------------------------------------

    [Fact(Skip = "Documents open finding #2: non-finite coords produce NaN, and NaN > radius is false → geofence bypass. Un-skip when the scan endpoint validates coordinates.")]
    public void Non_finite_coordinates_must_not_produce_NaN()
    {
        // A client posting 1e400 parses to double.PositiveInfinity; Haversine turns that into NaN,
        // and every `distance > radius` comparison against NaN is FALSE → the scan is accepted from
        // anywhere on earth. The fix belongs at the endpoint (reject non-finite input), so this test
        // is the marker for it, not the fix.
        var d = GeoCalculator.DistanceMeters(double.PositiveInfinity, double.PositiveInfinity, Lat1, Lon1);
        Assert.False(double.IsNaN(d), "non-finite input must not silently become NaN");
    }
}
