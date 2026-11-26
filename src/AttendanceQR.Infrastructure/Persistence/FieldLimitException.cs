using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Persistence;

/// <summary>
/// A value that does not fit its column, refused by <see cref="FieldLimits"/> before the save reaches the database.
///
/// A <see cref="DbUpdateException"/> on purpose: it stands in for the error Postgres would have raised, so every
/// <c>catch (DbUpdateException)</c> that exists today — the best-effort writes after a check-in, the races on a
/// unique index — sees exactly what it saw before. Only the type is sharper, so the API can answer 400 with the
/// field and its limit instead of an empty 500.
///
/// The message names the entity, the property and the limit, never the value: the value is somebody's text, and
/// this message ends up in the logs.
/// </summary>
public abstract class FieldLimitException : DbUpdateException
{
    protected FieldLimitException(string entity, string property, string message) : base(message)
    {
        Entity = entity;
        Property = property;
    }

    /// <summary>The entity's CLR name, e.g. «NonWorkingDay».</summary>
    public string Entity { get; }

    /// <summary>The property's CLR name, e.g. «Description».</summary>
    public string Property { get; }
}

/// <summary>Text longer than its varchar(n) column. Postgres would answer 22001 «value too long».</summary>
public sealed class FieldTooLongException : FieldLimitException
{
    public FieldTooLongException(string entity, string property, int maxLength)
        : base(entity, property, $"{entity}.{property} is longer than its column allows ({maxLength} characters).")
    {
        MaxLength = maxLength;
    }

    public int MaxLength { get; }
}

/// <summary>A number too large for its numeric(p,s) column. Postgres would answer 22003 «numeric field overflow».</summary>
public sealed class NumberTooLargeException : FieldLimitException
{
    public NumberTooLargeException(string entity, string property, int precision, int scale, decimal max)
        : base(entity, property, $"{entity}.{property} does not fit numeric({precision},{scale}); the largest value is {max}.")
    {
        Precision = precision;
        Scale = scale;
        Max = max;
    }

    public int Precision { get; }
    public int Scale { get; }

    /// <summary>The largest value the column holds, e.g. 99999999.99 for numeric(10,2).</summary>
    public decimal Max { get; }
}
