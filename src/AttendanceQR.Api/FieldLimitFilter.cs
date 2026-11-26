using System.Text.Json;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AttendanceQR.Api;

/// <summary>
/// The one answer to «this does not fit its column», wherever it comes from:
/// <c>400 {"error":"TextTooLong","field":"description","max":200}</c>, or <c>NumberTooLarge</c> for a money column.
///
/// Before this, a value one character too long reached Postgres and came back as an empty 500, and every screen said
/// its generic «Yadda saxlanmadı» — so the admin could not know that shortening the text was the whole fix.
/// Controllers that check what somebody typed answer with <see cref="Body"/> themselves; the filter is the net for
/// every other save, through <see cref="FieldLimits"/> in AppDbContext.
///
/// The Postgres codes are mapped too, for a write that does not pass through SaveChanges (ExecuteUpdate) and so is
/// never seen by FieldLimits. Postgres does not name the column for these, so that body has no field.
/// </summary>
public static class FieldLimitErrors
{
    public static object Body(FieldLimitException e) => e switch
    {
        FieldTooLongException t => new { error = "TextTooLong", field = Camel(t.Property), max = t.MaxLength },
        NumberTooLargeException n => new { error = "NumberTooLarge", field = Camel(n.Property), max = n.Max },
        _ => new { error = "TextTooLong", field = Camel(e.Property) },
    };

    /// <summary>The 400 body for <paramref name="e"/> and the exception in its chain that says so, or null when it is
    /// not a column limit.</summary>
    public static (object Body, Exception Cause)? Find(Exception e)
    {
        for (var x = (Exception?)e; x is not null; x = x.InnerException)
        {
            switch (x)
            {
                case FieldLimitException limit:
                    return (Body(limit), limit);
                case PostgresException { SqlState: PostgresErrorCodes.StringDataRightTruncation } pg:
                    return (new { error = "TextTooLong" }, pg);
                case PostgresException { SqlState: PostgresErrorCodes.NumericValueOutOfRange } pg:
                    return (new { error = "NumberTooLarge" }, pg);
            }
        }
        return null;
    }

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}

/// <summary>Registered once, for every controller (Program.cs). See <see cref="FieldLimitErrors"/>.</summary>
public sealed class FieldLimitFilter : IExceptionFilter
{
    private readonly ILogger<FieldLimitFilter> _logger;

    public FieldLimitFilter(ILogger<FieldLimitFilter> logger) => _logger = logger;

    public void OnException(ExceptionContext context)
    {
        if (FieldLimitErrors.Find(context.Exception) is not { } found)
            return;
        // A warning, not an error: the request was wrong, not the server. Both messages name the column and its limit
        // — never the value, which is somebody's text.
        _logger.LogWarning("Refused a value that does not fit its column: {Message}", found.Cause.Message);
        context.Result = new BadRequestObjectResult(found.Body);
        context.ExceptionHandled = true;
    }
}
