using AttendanceQR.Api.Approvals;
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
///
/// Not only the plan's any more. Every request that changes a past day — a leave, a void, a manual day, a holiday —
/// used to commit the change and then rebuild on the request's own cancellation token, with no note. Closing the tab
/// or backgrounding the phone app mid-rebuild left every day after that point on its old summary: a sick leave filed
/// for last week still read Qayıb and payroll deducted it, and a retry could not help (the leave was already there,
/// so it answered «AllOverlap» before rebuilding anything). Those requests now go through
/// <see cref="SaveWithNotesAsync"/> and <see cref="FinishAsync"/>.
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

    /// <summary>
    /// Saves the caller's change with a note for each of its days, in one save — use it in place of the change's own
    /// SaveChanges, then <see cref="FinishAsync"/>.
    ///
    /// The notes are unique per company and day. Two requests touching the same day in the same instant both find it
    /// un-noted and both insert one, and Postgres refuses the second with 23505 — which, unhandled, would throw away
    /// that request's whole change (a crew's leave, a void) over a bookkeeping row. The day is noted either way, so ours
    /// are dropped and the change is saved on its own; the rebuild that follows covers the day. (The in-memory test
    /// provider never raises this; a test stands in for Postgres with an interceptor.)
    /// </summary>
    public static async Task SaveWithNotesAsync(AppDbContext db, IEnumerable<DateOnly> dates, CancellationToken ct)
    {
        await MarkAsync(db, dates, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ChangeRequestDesk.UniqueViolation(ex) is { } index
                                           && index.Contains(nameof(AppDbContext.PendingSummaryRebuilds)))
        {
            foreach (var note in db.ChangeTracker.Entries<PendingSummaryRebuild>()
                         .Where(e => e.State == EntityState.Added).ToList())
                note.State = EntityState.Detached;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// <see cref="RunAsync"/> for a request whose change is already committed: it runs to the end whether or not the
    /// person who asked is still waiting. The request's own token is the wrong one here — it fires when somebody closes
    /// the tab — and the change it would leave half-applied is already in the database. A day it cannot rebuild keeps
    /// its note, and SummaryRebuildWorker finishes it within a couple of minutes.
    /// </summary>
    public static Task<(int Rebuilt, int Pending)> FinishAsync(
        AppDbContext db, IDailySummaryService summaries, IEnumerable<DateOnly> dates, ILogger? log) =>
        RunAsync(db, summaries, dates, log, CancellationToken.None);

    /// <summary>
    /// The days of a range that have a stored summary to rebuild — the past ones. Today and the future are computed
    /// live and never stored (GenerateForDateAsync refuses them), so noting them would only be work for nobody; a leave
    /// a year ahead would otherwise write 366 notes.
    /// </summary>
    public static List<DateOnly> PastDays(DateOnly from, DateOnly to, DateOnly today)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to && d < today; d = d.AddDays(1))
            days.Add(d);
        return days;
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
