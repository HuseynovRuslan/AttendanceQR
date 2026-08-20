using AttendanceQR.Api.Jobs;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Pins the retry schedule of the durable photo queue. The shape matters operationally: early
/// retries must be quick (a blip should cost seconds), the cap must be short enough that recovery
/// after a long outage starts within minutes of storage returning, and the total budget must
/// actually cover an outage measured in hours before a photo is declared lost.
/// </summary>
public class PhotoUploadBackoffTests
{
    [Theory]
    [InlineData(1, 30)]     // first failure — try again in half a minute
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 300)]    // capped from here on
    [InlineData(6, 300)]
    [InlineData(60, 300)]
    public void Backoff_doubles_then_caps_at_five_minutes(int attempt, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, PhotoUploadWorker.BackoffFor(attempt).TotalSeconds);
    }

    [Fact]
    public void Zero_and_negative_attempts_do_not_explode_the_exponent()
    {
        Assert.Equal(30, PhotoUploadWorker.BackoffFor(0).TotalSeconds);
        Assert.Equal(30, PhotoUploadWorker.BackoffFor(-3).TotalSeconds);
    }

    [Fact]
    public void The_retry_budget_covers_an_outage_of_hours_not_minutes()
    {
        // Sum of the whole schedule ≈ how long storage may stay down before photos are declared
        // lost. The exact figure may drift with tuning; the ORDER OF MAGNITUDE must not.
        var total = TimeSpan.Zero;
        for (var a = 1; a < PhotoUploadWorker.MaxAttempts; a++)
            total += PhotoUploadWorker.BackoffFor(a);
        Assert.True(total > TimeSpan.FromHours(4), $"retry budget only covers {total}");
    }
}
