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

    // --- the geofence bypass (audit finding #2) ---------------------------------------------------

    [Theory]
    [InlineData(double.PositiveInfinity, 49.8)]  // what JSON "1e400" deserializes to
    [InlineData(double.NegativeInfinity, 49.8)]
    [InlineData(double.NaN, 49.8)]
    [InlineData(40.3, double.PositiveInfinity)]
    [InlineData(40.3, double.NaN)]
    public void Non_finite_coordinates_are_rejected_rather_than_returning_NaN(double lat, double lon)
    {
        // The bypass this guards: Haversine maps non-finite input to NaN, and the caller's
        // `distance > radius` is FALSE against NaN — so the scan reads as inside the fence from
        // anywhere on earth. Never hand back a value that silently disables the check.
        Assert.Throws<ArgumentException>(() => GeoCalculator.DistanceMeters(lat, lon, Lat1, Lon1));
    }

    [Fact]
    public void Non_finite_target_coordinates_are_rejected_too()
    {
        // The location side comes from the DB, but a bad row must fail loudly, not open the fence.
        Assert.Throws<ArgumentException>(
            () => GeoCalculator.DistanceMeters(Lat1, Lon1, double.PositiveInfinity, Lon1));
    }
}
