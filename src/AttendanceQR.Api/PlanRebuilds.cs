using AttendanceQR.Application.Reporting;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Api;

/// <summary>
/// Worked days whose plan changed, rebuilt so the tabel and the pay agree with the plan.
///
/// The order is what keeps them consistent. The plan and a <see cref="PendingSummaryRebuild"/> row per worked day go
/// in ONE save, so there is never a changed plan without a note that its days are stale. Then each day is rebuilt —
/// one save per day, so a day is either rebuilt whole or left exactly as it was — and its note is removed. A rebuild
/// that fails leaves the note, and <c>SummaryRebuildWorker</c> keeps trying it: the stored day is never half-changed,
/// and never silently left behind.
/// </summary>
public static class PlanRebuilds
{
    /// <summary>Notes the days as stale, in the caller's unsaved changes.</summary>
    public static async Task MarkAsync(AppDbContext db, IEnumerable<DateOnly> dates, CancellationToken ct)
    {
        var list = dates.Distinct().ToList();
        if (list.Count == 0)
            return;
        var noted = await db.PendingSummaryRebuilds.Where(r => list.Contains(r.Date)).Select(r => r.Date).ToListAsync(ct);
        foreach (var date in list.Except(noted))
            db.PendingSummaryRebuilds.Add(new PendingSummaryRebuild { Date = date });
    }

    /// <summary>Rebuilds the days now; returns how many held and how many are still waiting.</summary>
    public static async Task<(int Rebuilt, int Pending)> RunAsync(
        AppDbContext db, IDailySummaryService summaries, IEnumerable<DateOnly> dates, ILogger? log, CancellationToken ct)
    {
        int rebuilt = 0, pending = 0;
        foreach (var date in dates.Distinct().Order())
        {
            try
            {
                await summaries.GenerateForDateAsync(date, ct);
                var note = await db.PendingSummaryRebuilds.FirstOrDefaultAsync(r => r.Date == date, ct);
                if (note is not null)
                {
                    db.PendingSummaryRebuilds.Remove(note);
                    await db.SaveChangesAsync(ct);
                }
                rebuilt++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Whatever the failed rebuild left half-tracked is dropped; the note stays for the worker.
                db.ChangeTracker.Clear();
                try
                {
                    if (await db.PendingSummaryRebuilds.FirstOrDefaultAsync(r => r.Date == date, ct) is { } note)
                    {
                        note.Attempts++;
                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception inner) when (inner is not OperationCanceledException)
                {
                    log?.LogWarning(inner, "PlanRebuilds: could not count a failed attempt for {Date}", date);
                }
                log?.LogError(ex, "PlanRebuilds: {Date} not rebuilt yet; the worker will try again", date);
                pending++;
            }
        }
        return (rebuilt, pending);
    }

    /// <summary>The days still waiting, oldest note first — what the worker picks up.</summary>
    public static Task<List<DateOnly>> WaitingAsync(AppDbContext db, TimeSpan olderThan, CancellationToken ct)
    {
        var before = DateTime.UtcNow - olderThan;
        return db.PendingSummaryRebuilds
            .Where(r => r.RequestedAtUtc < before && r.Attempts < 50)
            .OrderBy(r => r.RequestedAtUtc)
            .Select(r => r.Date)
            .Take(31)
            .ToListAsync(ct);
    }
}
