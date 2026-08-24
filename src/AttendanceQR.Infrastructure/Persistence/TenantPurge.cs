using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AttendanceQR.Infrastructure.Persistence;

/// <summary>
/// Removing every row a company owns.
///
/// The tables are not listed here. They are read out of EF's own model — every entity type that
/// carries a TenantId — because a hand-written list is exactly the thing that goes stale: TaskItem
/// already implements ITenantScoped and already has a tenant query filter, yet it is missing from the
/// tenantScoped array in AppDbContext, so it has no FK and no index. A copied list would have left its
/// rows behind, pointing at a company that no longer exists, and nothing would have complained.
///
/// Two things are deliberately NOT reached by that rule. SuperAdminAuditLog names its column
/// TargetTenantId, so it survives — it is the record that the deletion happened, and a record that
/// disappears with what it describes is not a record. And the Tenant row itself has no TenantId; the
/// caller removes it last, once the rows that point at it are gone.
///
/// Deletes are tracked rather than bulk (ExecuteDelete), for two reasons: EF then orders them by
/// dependency itself, so no FK graph has to be maintained by hand, and the same code runs against the
/// in-memory provider the tests use. It is only ever pointed at a company with no attendance history,
/// so the row counts are small.
/// </summary>
public static class TenantPurge
{
    private static readonly MethodInfo SetMethod = typeof(DbContext)
        .GetMethods()
        .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0);

    private static readonly MethodInfo PurgeOne = typeof(TenantPurge)
        .GetMethod(nameof(PurgeTypeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Every entity type that belongs to a company, straight from the model.</summary>
    public static IReadOnlyList<IEntityType> ScopedTypes(IModel model) =>
        model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.FindProperty("TenantId") is not null)
            .OrderBy(t => t.ClrType.Name)
            .ToList();

    /// <summary>
    /// How many rows this company owns, per table. Shown to the operator before they confirm, because
    /// "this will delete 3 employees and 1 branch" is a different decision from "this will delete 64
    /// employees" — and a wrong company deleted quietly is the whole risk of this feature.
    /// </summary>
    public static async Task<Dictionary<string, int>> CountAsync(DbContext db, Guid tenantId, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>();
        foreach (var type in ScopedTypes(db.Model))
        {
            var rows = await RowsAsync(db, type, tenantId, ct);
            if (rows.Count > 0) counts[type.ClrType.Name] = rows.Count;
        }
        return counts;
    }

    /// <summary>
    /// Removes every row belonging to the company. Does NOT remove the Tenant row, does not save, and
    /// does not touch object storage — the caller collects photo keys first (they are unrecoverable
    /// afterwards) and purges the bucket after the database has committed.
    /// </summary>
    public static async Task<int> PurgeAsync(DbContext db, Guid tenantId, CancellationToken ct)
    {
        var total = 0;
        foreach (var type in ScopedTypes(db.Model))
        {
            var task = (Task<int>)PurgeOne.MakeGenericMethod(type.ClrType).Invoke(null, [db, tenantId, ct])!;
            total += await task;
        }
        return total;
    }

    private static async Task<List<object>> RowsAsync(DbContext db, IEntityType type, Guid tenantId, CancellationToken ct)
    {
        var task = (Task<List<object>>)typeof(TenantPurge)
            .GetMethod(nameof(RowsOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(type.ClrType)
            .Invoke(null, [db, tenantId, ct])!;
        return await task;
    }

    private static async Task<List<object>> RowsOfAsync<T>(DbContext db, Guid tenantId, CancellationToken ct)
        where T : class
    {
        var rows = await Query<T>(db, tenantId).AsNoTracking().ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    private static async Task<int> PurgeTypeAsync<T>(DbContext db, Guid tenantId, CancellationToken ct)
        where T : class
    {
        var rows = await Query<T>(db, tenantId).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        db.RemoveRange(rows);
        return rows.Count;
    }

    /// <summary>
    /// IgnoreQueryFilters is mandatory here, and is the reason this is a purpose-built helper rather
    /// than ordinary code: the request runs as the OPERATOR, whose resolved tenant is their own
    /// company, so the filter would silently match nothing and report a clean sweep of zero rows.
    /// </summary>
    private static IQueryable<T> Query<T>(DbContext db, Guid tenantId) where T : class =>
        ((IQueryable<T>)SetMethod.MakeGenericMethod(typeof(T)).Invoke(db, null)!)
            .IgnoreQueryFilters()
            .Where(e => EF.Property<Guid>(e, "TenantId") == tenantId);
}
