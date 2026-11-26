using System.Data.Common;
using AttendanceQR.Api.Contracts;
using AttendanceQR.Api.Controllers;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// A branch created with no working days is stored with no working days.
///
/// Locations.WorkDaysMask has a database default (126, Monday–Saturday) for the backfill, and EF leaves a
/// column with a database default out of the INSERT whenever the property holds its sentinel — the CLR
/// default, 0, until told otherwise. But 0 is a real mask: the admin unticked every day because the people's
/// own shifts decide. Postgres then filled in 126, and the branch claimed six working days nobody had set.
///
/// The in-memory provider ignores database defaults, so a create-and-read-back test passed either way. These
/// tests build the real PostgreSQL INSERT instead — the connection is never opened and the command never runs;
/// an interceptor reads the SQL EF would have sent and stops there.
/// </summary>
public class BranchWorkDaysInsertTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-00000000d0a5");

    /// <summary>Thrown by the interceptor once it has the SQL, so nothing is ever sent anywhere.</summary>
    private sealed class Captured : Exception;

    /// <summary>Pretends the connection opened, and keeps the first command instead of running it.</summary>
    private sealed class CaptureFirstCommand : DbCommandInterceptor
    {
        public string? Sql { get; private set; }
        public Dictionary<string, object?> Parameters { get; } = new();

        private Captured Keep(DbCommand command)
        {
            Sql ??= command.CommandText;
            foreach (DbParameter p in command.Parameters)
                Parameters.TryAdd(p.ParameterName, p.Value);
            return new Captured();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) => throw Keep(command);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) => throw Keep(command);

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result) => throw Keep(command);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw Keep(command);
    }

    private sealed class NeverConnect : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) => new(InterceptionResult.Suppress());
    }

    /// <summary>The INSERT the create endpoint would send to PostgreSQL for a branch with this mask.</summary>
    private static async Task<CaptureFirstCommand> InsertSentFor(int workDaysMask)
    {
        var capture = new CaptureFirstCommand();
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none")
                .AddInterceptors(new NeverConnect(), capture)
                .Options,
            tenant);

        var controller = new AdminLocationsController(
            db,
            new QrTokenService(Options.Create(new QrTokenOptions { Secret = "test-secret-for-branch-work-days", TtlSeconds = 60 })),
            new OffFaceMatch(),
            NullLogger<AdminLocationsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var request = new LocationRequest("Yeni filial", 40.41, 49.85, 150, "09:00", "18:00", 15, workDaysMask);
        await Assert.ThrowsAnyAsync<Exception>(() => controller.Create(request));
        Assert.NotNull(capture.Sql);
        return capture;
    }

    /// <summary>The column list of an INSERT, between «INSERT INTO "Locations" (» and the first «)».</summary>
    private static string[] InsertedColumns(string sql)
    {
        var insert = sql.IndexOf("INSERT INTO \"Locations\"", StringComparison.Ordinal);
        Assert.True(insert >= 0, sql);
        var open = sql.IndexOf('(', insert);
        var close = sql.IndexOf(')', open);
        return sql[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The value sent for one column: VALUES lines up with the column list, parameter for parameter.</summary>
    private static object? SentValue(CaptureFirstCommand insert, string column)
    {
        var columns = InsertedColumns(insert.Sql!);
        var index = Array.IndexOf(columns, $"\"{column}\"");
        Assert.True(index >= 0, $"{column} is not in the INSERT, so the database default would be stored: {insert.Sql}");
        var values = insert.Sql![insert.Sql!.IndexOf("VALUES", StringComparison.Ordinal)..];
        var open = values.IndexOf('(');
        var close = values.IndexOf(')', open);
        var parameter = values[(open + 1)..close].Split(',', StringSplitOptions.TrimEntries)[index];
        Assert.True(insert.Parameters.TryGetValue(parameter, out var value)
                    || insert.Parameters.TryGetValue(parameter.TrimStart('@'), out value), $"no parameter {parameter}");
        return value;
    }

    [Fact]
    public async Task A_branch_with_no_working_days_is_inserted_with_no_working_days()
    {
        var insert = await InsertSentFor(workDaysMask: 0);

        Assert.Equal(0, SentValue(insert, nameof(Location.WorkDaysMask)));
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    [InlineData(1)]   // Sundays only
    public async Task Every_other_mask_is_inserted_as_given_too(int mask)
    {
        var insert = await InsertSentFor(mask);

        Assert.Equal(mask, SentValue(insert, nameof(Location.WorkDaysMask)));
    }

    [Fact]
    public void No_mask_an_admin_can_save_is_the_value_that_means_use_the_database_default()
    {
        // TryValidate accepts 0..127. A mask equal to the sentinel would be dropped from the INSERT and replaced
        // by 126 — the trap above — so the sentinel must sit outside that range.
        var tenant = new TenantContext();
        tenant.Resolve(TenantId);
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none").Options,
            tenant);
        var property = db.Model.FindEntityType(typeof(Location))!.FindProperty(nameof(Location.WorkDaysMask))!;

        Assert.Equal(126, property.GetDefaultValue());
        Assert.DoesNotContain(property.Sentinel, Enumerable.Range(0, 128).Cast<object>());
    }
}
