using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// What Postgres would refuse and the in-memory store does not. The suite runs on EF Core InMemory, which enforces no
/// unique index at all — so a fix for a 23505 is otherwise tested against a database that could never have raised it.
/// These stand the constraint in at save time, with the same exception shape Npgsql throws.
/// </summary>
internal static class PostgresStandIns
{
    /// <summary>The DbUpdateException a unique violation on <paramref name="constraint"/> arrives as.</summary>
    public static DbUpdateException UniqueViolation(string constraint) => new(
        "simulated: 23505 duplicate key value violates unique constraint",
        new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR",
            PostgresErrorCodes.UniqueViolation, constraintName: constraint));
}

/// <summary>
/// IX_AttendanceRecords_OneOpenPerDay: unique (EmployeeId, AttendanceDate) WHERE "CheckOutAtUtc" IS NULL. Voided rows
/// count — the filter does not mention them, exactly as in production. Checked against the committed rows (read through
/// a second context on the same store) with this save's changes laid over them.
/// </summary>
internal sealed class OneOpenPerDayIndex(Func<AppDbContext> committed) : SaveChangesInterceptor
{
    public int Refusals { get; private set; }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Check(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Check(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    private void Check(DbContext db)
    {
        var changes = db.ChangeTracker.Entries<AttendanceRecord>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changes.Count == 0)
            return;

        using var store = committed();
        var rows = store.AttendanceRecords.AsNoTracking()
            .Select(r => new { r.Id, r.EmployeeId, r.AttendanceDate, Open = r.CheckOutAtUtc == null })
            .ToList()
            .ToDictionary(r => r.Id, r => (r.EmployeeId, r.AttendanceDate, r.Open));
        foreach (var change in changes)
        {
            if (change.State == EntityState.Deleted)
                rows.Remove(change.Entity.Id);
            else
                rows[change.Entity.Id] = (change.Entity.EmployeeId, change.Entity.AttendanceDate, change.Entity.CheckOutAtUtc == null);
        }

        if (rows.Values.Where(r => r.Open).GroupBy(r => (r.EmployeeId, r.AttendanceDate)).Any(g => g.Count() > 1))
        {
            Refusals++;
            throw PostgresStandIns.UniqueViolation("IX_AttendanceRecords_OneOpenPerDay");
        }
    }
}

/// <summary>
/// IX_PendingSummaryRebuilds_TenantId_Date, lost once: the first save that adds a note is refused as if another
/// request had noted the same day a moment earlier — the race the in-memory store can never produce.
/// </summary>
internal sealed class NoteTakenOnce : SaveChangesInterceptor
{
    public bool Refused { get; private set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (!Refused && eventData.Context!.ChangeTracker.Entries<PendingSummaryRebuild>().Any(e => e.State == EntityState.Added))
        {
            Refused = true;
            throw PostgresStandIns.UniqueViolation("IX_PendingSummaryRebuilds_TenantId_Date");
        }
        return ValueTask.FromResult(result);
    }
}
