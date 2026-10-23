using System.Collections.Concurrent;

namespace AttendanceQR.Infrastructure.Services;

/// <summary>
/// Days whose stored summary is out of date because a scan landed on them after they were summarised.
///
/// A finished day is read from DailySummaries, never recomputed on the fly, and the nightly job writes
/// yesterday at 00:30. A scan that reaches the server AFTER that — an offline check-out from last night
/// sent the next morning, a check-in from a weekend with no signal sent on Monday — wrote its record and
/// changed nothing anybody could see: the tabel and the payroll still read the day as Qayıb or zero
/// hours. The field-visit close had already learned this and rebuilds its day; the scan path had not.
///
/// A request only marks the day. The rebuild runs in the background a few seconds later, once per day
/// however many scans asked for it — the morning an offline crew phone comes back with thirty people's
/// taps is the moment not to rebuild the same day thirty times on the scan path.
/// </summary>
public interface ISummaryRebuildQueue
{
    /// <summary>Mark this day of this company for a rebuild. Cheap, never throws, idempotent.</summary>
    void Request(Guid tenantId, DateOnly date);

    /// <summary>Every day requested at least <paramref name="settle"/> ago — removed from the set.</summary>
    IReadOnlyList<(Guid TenantId, DateOnly Date)> TakeSettled(TimeSpan settle, DateTime nowUtc);
}

public sealed class SummaryRebuildQueue : ISummaryRebuildQueue
{
    // Value = when it was last asked for. A day asked for again resets its clock, so a burst is rebuilt
    // once, after it ends.
    private readonly ConcurrentDictionary<(Guid TenantId, DateOnly Date), DateTime> _pending = new();

    public void Request(Guid tenantId, DateOnly date) => _pending[(tenantId, date)] = DateTime.UtcNow;

    public IReadOnlyList<(Guid TenantId, DateOnly Date)> TakeSettled(TimeSpan settle, DateTime nowUtc)
    {
        var due = new List<(Guid, DateOnly)>();
        foreach (var entry in _pending)
        {
            // Removed only if nobody asked again since it was read — that request keeps its turn.
            if (nowUtc - entry.Value >= settle && _pending.TryRemove(entry))
                due.Add(entry.Key);
        }
        return due;
    }
}
