using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AttendanceQR.Infrastructure.Persistence;

/// <summary>
/// The rows that hold another row in place: every foreign key in the model that points at a given entity
/// type and that the DATABASE will neither cascade nor null when that row is deleted. While one of them
/// names the row, Postgres refuses the DELETE (23503).
///
/// Read out of EF's model rather than listed by hand, for the reason <see cref="TenantPurge"/> gives: a
/// hand-written list is exactly the thing that goes stale. The employee delete checked three tables;
/// role assignments and PIN-reset requests arrived later with Restrict keys to Employee, nobody added
/// them, and every delete of a former manager or of anyone who had pressed «PIN-i unutdum» became an
/// unhandled 500 in production — the in-memory test provider enforces no keys, so nothing failed first.
///
/// Callers decide what each reference MEANS (history to refuse over, a row to remove with its owner, a
/// pointer to clear); this only finds them, counts them and acts on them. Everything is tracked, not
/// bulk, so it all lands in the caller's single SaveChanges — one transaction, all or nothing — and the
/// same code runs on the in-memory provider the tests use.
/// </summary>
public static class RowReferences
{
    /// <summary>One foreign key that holds a <paramref name="Principal"/> row in place.</summary>
    /// <param name="ForeignKey">The key as the model declares it.</param>
    /// <param name="Property">The dependent column that carries the principal's id — for a composite key
    /// such as (TenantId, LocationId), the LocationId half.</param>
    public sealed record Reference(Type Principal, IForeignKey ForeignKey, IProperty Property)
    {
        /// <summary>«RoleAssignment.EmployeeId» — stable, readable, and what callers key their policy on.</summary>
        public string Name => $"{ForeignKey.DeclaringEntityType.ClrType.Name}.{Property.Name}";

        /// <summary>The table the rows live in, as Postgres names it.</summary>
        public string Table => ForeignKey.DeclaringEntityType.GetTableName() ?? ForeignKey.DeclaringEntityType.ClrType.Name;

        public bool Nullable => Property.IsNullable;
    }

    private static readonly MethodInfo SetMethod = typeof(DbContext)
        .GetMethods()
        .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0);

    private static readonly MethodInfo CountOne = typeof(RowReferences)
        .GetMethod(nameof(CountTypeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo LoadOne = typeof(RowReferences)
        .GetMethod(nameof(LoadTypeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Every foreign key to <paramref name="principal"/> whose delete rule leaves the work to the
    /// application: Restrict and NoAction, and EF's Client* rules, which the database sees as NO ACTION.
    /// Cascade and SetNull are the database's own business and are left out.
    /// </summary>
    public static IReadOnlyList<Reference> Holding(IModel model, Type principal)
    {
        var principalType = model.FindEntityType(principal)
            ?? throw new ArgumentException($"{principal.Name} is not in the model", nameof(principal));
        var idName = principalType.FindPrimaryKey()!.Properties.Single().Name;

        return model.GetEntityTypes()
            .Where(t => !t.IsOwned())
            .SelectMany(t => t.GetDeclaredForeignKeys())
            .Where(fk => fk.PrincipalEntityType == principalType)
            .Where(fk => fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull))
            .Select(fk =>
            {
                // The dependent column that pairs with the principal's Id. A composite key to a company-
                // scoped branch is (TenantId, LocationId) → (TenantId, Id): the TenantId half is implied.
                var principalProps = fk.PrincipalKey.Properties;
                var i = principalProps.ToList().FindIndex(p => p.Name == idName);
                return new Reference(principal, fk, fk.Properties[i]);
            })
            .OrderBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>How many rows of this reference name <paramref name="id"/>. Under the caller's query filters.</summary>
    public static Task<int> CountAsync(DbContext db, Reference reference, Guid id, CancellationToken ct = default)
        => (Task<int>)CountOne.MakeGenericMethod(reference.ForeignKey.DeclaringEntityType.ClrType)
            .Invoke(null, [db, reference.Property, id, ct])!;

    /// <summary>Marks every row of this reference that names <paramref name="id"/> for deletion. Does not save.</summary>
    public static async Task<int> RemoveAsync(DbContext db, Reference reference, Guid id, CancellationToken ct = default)
    {
        var rows = await LoadAsync(db, reference, id, ct);
        foreach (var row in rows)
            db.Remove(row);
        return rows.Count;
    }

    /// <summary>
    /// Clears this reference on every row that names <paramref name="id"/>, keeping the row. A row already
    /// marked for deletion (someone who reviewed their own request) is left to go. Does not save.
    /// </summary>
    public static async Task<int> DetachAsync(DbContext db, Reference reference, Guid id, CancellationToken ct = default)
    {
        if (!reference.Nullable)
            throw new InvalidOperationException($"{reference.Name} is required and cannot be cleared");

        var cleared = 0;
        foreach (var row in await LoadAsync(db, reference, id, ct))
        {
            var entry = db.Entry(row);
            if (entry.State == EntityState.Deleted) continue;
            entry.Property(reference.Property.Name).CurrentValue = null;
            cleared++;
        }
        return cleared;
    }

    private static async Task<List<object>> LoadAsync(DbContext db, Reference reference, Guid id, CancellationToken ct)
    {
        var task = (Task<List<object>>)LoadOne.MakeGenericMethod(reference.ForeignKey.DeclaringEntityType.ClrType)
            .Invoke(null, [db, reference.Property, id, ct])!;
        return await task;
    }

    private static Task<int> CountTypeAsync<T>(DbContext db, IProperty property, Guid id, CancellationToken ct)
        where T : class => Query<T>(db, property, id).CountAsync(ct);

    private static async Task<List<object>> LoadTypeAsync<T>(DbContext db, IProperty property, Guid id, CancellationToken ct)
        where T : class => (await Query<T>(db, property, id).ToListAsync(ct)).Cast<object>().ToList();

    /// <summary>
    /// <c>rows.Where(r => r.{property} == id)</c>, built as an expression so it works for Guid and Guid?
    /// alike. The id goes in through a captured field, so EF sends it as a parameter, not a literal.
    /// </summary>
    private static IQueryable<T> Query<T>(DbContext db, IProperty property, Guid id) where T : class
    {
        var set = (IQueryable<T>)SetMethod.MakeGenericMethod(typeof(T)).Invoke(db, null)!;
        var row = Expression.Parameter(typeof(T), "r");
        Expression column = property.PropertyInfo is { } pi
            ? Expression.Property(row, pi)
            : Expression.Call(typeof(EF), nameof(EF.Property), [property.ClrType], row, Expression.Constant(property.Name));
        var box = new IdBox { Value = id };
        var value = Expression.Field(Expression.Constant(box), nameof(IdBox.Value));
        var equal = Expression.Equal(Expression.Convert(column, typeof(Guid?)), value);
        return set.Where(Expression.Lambda<Func<T, bool>>(equal, row));
    }

    private sealed class IdBox
    {
        public Guid? Value;
    }
}
