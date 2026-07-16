using System.ComponentModel.DataAnnotations;
using System.Reflection;
using AttendanceQR.Api.Contracts;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Proves the coordinate bounds on the request contracts actually run under MVC.
///
/// The first version of these tests used Validator.TryValidateObject and passed while production was
/// throwing on every scan. TryValidateObject reads PROPERTIES; MVC reads the record's CONSTRUCTOR
/// PARAMETERS and throws outright if it finds validation metadata on a property instead. So the test
/// could not tell the broken arrangement from the working one — it agreed with whatever was written.
/// Hence <see cref="Bounds_are_on_the_constructor_parameters_which_is_what_MVC_reads"/>, which pins
/// the placement itself. The value assertions below still use TryValidateObject, which is fine for
/// asking "does this RangeAttribute reject infinity" once the placement is proven separately.
/// </summary>
public class ScanRequestValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        // Validates the constructor parameters' attributes by evaluating them directly — see above
        // for why this is NOT a stand-in for how MVC discovers them.
        var results = new List<ValidationResult>();
        var ctor = model.GetType().GetConstructors().Single();
        foreach (var p in ctor.GetParameters())
        {
            var attrs = p.GetCustomAttributes<ValidationAttribute>().ToArray();
            if (attrs.Length == 0) continue;
            var value = model.GetType().GetProperty(p.Name!)!.GetValue(model);
            var ctx = new ValidationContext(model) { MemberName = p.Name };
            Validator.TryValidateValue(value!, ctx, results, attrs);
        }
        return results;
    }

    [Theory]
    [InlineData(typeof(ScanRequest), "Latitude")]
    [InlineData(typeof(ScanRequest), "Longitude")]
    [InlineData(typeof(LocationRequest), "Latitude")]
    [InlineData(typeof(LocationRequest), "Longitude")]
    [InlineData(typeof(LocationRequest), "RadiusMeters")]
    public void Bounds_are_on_the_constructor_parameters_which_is_what_MVC_reads(Type record, string name)
    {
        // THE regression test. [property: Range(...)] here made MVC throw
        // "validation metadata must be associated with the constructor parameter" on every request,
        // i.e. a 500 before the action even ran. Both halves matter: present on the parameter, and
        // ABSENT from the property.
        var parameter = record.GetConstructors().Single().GetParameters().Single(p => p.Name == name);
        Assert.NotEmpty(parameter.GetCustomAttributes<ValidationAttribute>());

        var property = record.GetProperty(name)!;
        Assert.Empty(property.GetCustomAttributes<ValidationAttribute>());
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
