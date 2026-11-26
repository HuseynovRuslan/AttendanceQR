using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AttendanceQR.Infrastructure.Persistence;

/// <summary>
/// The column limits of the model — varchar(n) and numeric(p,s) — checked in C# before a save.
///
/// Why it exists. The tests run on EF's in-memory provider, which stores a string of any length and a decimal of
/// any size; Postgres refuses both. So a value that does not fit passed every test and failed only in production,
/// as an empty 500 with nothing to say which field. It happened: the role-change audit row (PR #69) was longer than
/// its column and took every role change down with it, with a green test suite.
///
/// <see cref="Enforce"/> runs inside every SaveChanges (AppDbContext, next to the tenant stamp), so the in-memory
/// tests now fail where Postgres would, and production gets a <see cref="FieldLimitException"/> that the API turns
/// into 400 with the field and its limit. The limits are read from the model, so a column changed in a
/// configuration is followed here without anyone remembering to.
///
/// .NET counts a string's length in UTF-16 units and Postgres counts characters, so an emoji counts double here.
/// That only ever makes this check stricter than the database, never looser.
/// </summary>
public static class FieldLimits
{
    /// <summary>Per entity type, the properties that have a limit at all — most have none, and the save path runs on
    /// every scan, so the model is walked once per type rather than once per save. Weak, so a model that goes away
    /// (the tests build several) takes its entry with it.</summary>
    private static readonly ConditionalWeakTable<IEntityType, IProperty[]> Limited = new();

    /// <summary>The longest text a property's column takes, or null when it has no limit (text, jsonb).</summary>
    public static int? MaxLength<TEntity>(DbContext db, string property) =>
        db.Model.FindEntityType(typeof(TEntity))?.FindProperty(property)?.GetMaxLength();

    /// <summary>
    /// Whether <paramref name="value"/> fits the column of <typeparamref name="TEntity"/>.<paramref name="property"/>:
    /// null when it does, else the exception the save would throw. For the places that check what somebody typed
    /// before building anything, so they can answer with the field rather than fail the whole request.
    /// </summary>
    public static FieldLimitException? Check<TEntity>(DbContext db, string property, object? value) =>
        db.Model.FindEntityType(typeof(TEntity))?.FindProperty(property) is { } p ? Violation(p, value) : null;

    /// <summary>
    /// <paramref name="value"/> rounded the way its numeric(p,s) column stores it (to s decimals, half away from zero),
    /// so an answer that echoes the saved value says what Postgres holds rather than what was sent.
    /// </summary>
    public static decimal RoundToColumn<TEntity>(DbContext db, string property, decimal value) =>
        db.Model.FindEntityType(typeof(TEntity))?.FindProperty(property)?.GetScale() is int scale and >= 0 and <= 28
            ? Math.Round(value, scale, MidpointRounding.AwayFromZero)
            : value;

    /// <summary>
    /// Throws the first value that does not fit, among the rows this save will write. A modified row is checked only
    /// on the columns that changed — those are the only ones the UPDATE sends.
    /// </summary>
    public static void Enforce(ChangeTracker tracker)
    {
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;
            foreach (var property in LimitedProperties(entry.Metadata))
            {
                var value = entry.Property(property);
                if (entry.State == EntityState.Modified && !value.IsModified)
                    continue;
                if (Violation(property, value.CurrentValue) is { } violation)
                    throw violation;
            }
        }
    }

    private static IProperty[] LimitedProperties(IEntityType type) =>
        Limited.GetValue(type, t => t.GetProperties().Where(HasLimit).ToArray());

    // A property with a value converter stores something else than its CLR value, so its CLR value says nothing
    // about whether the stored one fits. None of the limited columns has one today.
    private static bool HasLimit(IProperty p) =>
        p.GetValueConverter() is null
        && ((p.ClrType == typeof(string) && p.GetMaxLength() is not null)
            || ((Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(decimal) && p.GetPrecision() is not null));

    private static FieldLimitException? Violation(IProperty property, object? value)
    {
        var entity = property.DeclaringType.ClrType.Name;
        switch (value)
        {
            case string text when property.GetMaxLength() is int max && text.Length > max:
                return new FieldTooLongException(entity, property.Name, max);

            case decimal number when property.GetPrecision() is int precision:
                var scale = property.GetScale() ?? 0;
                if (scale is < 0 or > 28 || precision - scale is < 0 or > 28)
                    return null;
                // numeric(p,s) rounds to s decimals first (half away from zero) and then refuses anything with more
                // than p−s digits before the point: 99999999.995 is rounded up to 100000000.00 and refused.
                var largest = Pow10(precision - scale) - 1m / Pow10(scale);
                return Math.Abs(Math.Round(number, scale, MidpointRounding.AwayFromZero)) > largest
                    ? new NumberTooLargeException(entity, property.Name, precision, scale, largest)
                    : null;

            default:
                return null;
        }
    }

    private static decimal Pow10(int n)
    {
        var r = 1m;
        for (var i = 0; i < n; i++)
            r *= 10m;
        return r;
    }
}
