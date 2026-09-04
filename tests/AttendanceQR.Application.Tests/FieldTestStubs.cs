using AttendanceQR.Application.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Shared doubles for the FieldVisitController tests.
///
/// The controller gained a summary-rebuild dependency when closing a visit stopped being enough on
/// its own: a PAST day is read from DailySummaries rather than computed, so a visit corrected today
/// still reported the Qayıb the broken one produced. The tests do not exercise the rebuild — they
/// assert what the controller writes — so it is stubbed rather than run.
/// </summary>
internal sealed class StubSummaries : IDailySummaryService
{
    /// <summary>The dates a rebuild was asked for, so a test can assert one was requested at all.</summary>
    public List<DateOnly> Rebuilt { get; } = new();

    public Task<int> GenerateForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        Rebuilt.Add(date);
        return Task.FromResult(0);
    }
}

internal static class FieldTestLog
{
    public static readonly NullLogger<AttendanceQR.Api.Controllers.FieldVisitController> Logger = new();
}
