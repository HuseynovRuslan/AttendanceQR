using System.ComponentModel.DataAnnotations;
using AttendanceQR.Api.Contracts;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Proves the coordinate bounds on the request contracts actually run. This is not a formality: on a
/// positional record, a bare [Range] binds to the CONSTRUCTOR PARAMETER, not the property MVC
/// validates — it compiles, reads correctly, and silently never fires. These tests fail if the
/// [property:] prefix is ever dropped.
/// </summary>
public class ScanRequestValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static ScanRequest Scan(double lat, double lon) => new("qr", "fp", lat, lon);

    [Fact]
    public void A_real_baku_coordinate_is_valid()
        => Assert.Empty(Validate(Scan(40.3660, 49.8352)));

    [Theory]
    // 1e400 in a JSON body deserializes to PositiveInfinity — this is the actual attack payload.
    [InlineData(double.PositiveInfinity, 49.8352)]
    [InlineData(double.NegativeInfinity, 49.8352)]
    [InlineData(double.NaN, 49.8352)]
    [InlineData(40.3660, double.PositiveInfinity)]
    [InlineData(40.3660, double.NaN)]
    // Ordinary out-of-range values, which the same bounds catch.
    [InlineData(91, 49.8352)]
    [InlineData(-91, 49.8352)]
    [InlineData(40.3660, 181)]
    [InlineData(40.3660, -181)]
    public void A_coordinate_that_is_out_of_range_or_not_finite_is_invalid(double lat, double lon)
        => Assert.NotEmpty(Validate(Scan(lat, lon)));

    [Fact]
    public void The_bounds_are_inclusive_at_the_poles_and_the_antimeridian()
    {
        Assert.Empty(Validate(Scan(90, 180)));
        Assert.Empty(Validate(Scan(-90, -180)));
    }

    [Fact]
    public void Location_coordinates_are_bounded_too()
    {
        var ok = new LocationRequest("Baş ofis", 40.4093, 49.8671, 150, "09:00", "18:00", 15, 126);
        Assert.Empty(Validate(ok));

        var poisoned = new LocationRequest("Baş ofis", double.PositiveInfinity, 49.8671, 150, "09:00", "18:00", 15, 126);
        Assert.NotEmpty(Validate(poisoned));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_location_radius_must_be_positive(int radius)
    {
        // A zero/negative radius admits nobody — the geofence stops meaning anything.
        var r = new LocationRequest("Baş ofis", 40.4093, 49.8671, radius, "09:00", "18:00", 15, 126);
        Assert.NotEmpty(Validate(r));
    }
}
