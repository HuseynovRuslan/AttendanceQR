using AttendanceQR.Infrastructure.Services;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The morning a crew phone comes back online it replays thirty taps onto yesterday. That must be one
/// rebuild of yesterday, after the burst — not thirty on the scan path.
/// </summary>
public class SummaryRebuildQueueTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateOnly Yesterday = new(2026, 9, 17);

    [Fact]
    public void Thirty_requests_for_one_day_are_one_rebuild()
    {
        var q = new SummaryRebuildQueue();
        for (var i = 0; i < 30; i++) q.Request(Tenant, Yesterday);

        var due = q.TakeSettled(TimeSpan.Zero, DateTime.UtcNow.AddSeconds(1));

        Assert.Single(due);
        Assert.Empty(q.TakeSettled(TimeSpan.Zero, DateTime.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public void A_day_still_being_asked_for_waits_until_the_burst_ends()
    {
        var q = new SummaryRebuildQueue();
        q.Request(Tenant, Yesterday);

        Assert.Empty(q.TakeSettled(TimeSpan.FromSeconds(10), DateTime.UtcNow));
        Assert.Single(q.TakeSettled(TimeSpan.FromSeconds(10), DateTime.UtcNow.AddSeconds(11)));
    }

    [Fact]
    public void Two_companies_are_two_rebuilds()
    {
        var q = new SummaryRebuildQueue();
        q.Request(Tenant, Yesterday);
        q.Request(Guid.NewGuid(), Yesterday);

        Assert.Equal(2, q.TakeSettled(TimeSpan.Zero, DateTime.UtcNow.AddSeconds(1)).Count);
    }
}
