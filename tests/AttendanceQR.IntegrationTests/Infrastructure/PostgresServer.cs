using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AttendanceQR.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL server per test run, and a fresh database per caller.
///
/// The migrations are applied ONCE, into a template database; every test that asks for a database gets a
/// copy (<c>CREATE DATABASE … TEMPLATE …</c>, a file copy on the server — a fraction of a second). So every
/// test starts from exactly what a brand-new production database looks like after <c>Migrate()</c>, including
/// the rows the migrations themselves insert (the original tenant, "bax"), and no test can see another's rows.
/// </summary>
public static class PostgresServer
{
    private const string TemplateName = "qrlog_it_template";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _adminConnection;
    private static PostgreSqlContainer? _container;

    /// <summary>Creates an empty, fully migrated database and returns its connection string.</summary>
    public static async Task<string> CreateDatabaseAsync()
    {
        var admin = await EnsureTemplateAsync();
        var name = "qrlog_it_" + Guid.NewGuid().ToString("N")[..12];

        await using (var conn = new NpgsqlConnection(admin))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE \"{TemplateName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = true }.ConnectionString;
    }

    private static async Task<string> EnsureTemplateAsync()
    {
        if (_adminConnection is not null)
            return _adminConnection;

        await Gate.WaitAsync();
        try
        {
            if (_adminConnection is not null)
                return _adminConnection;

            var admin = await StartServerAsync();

            await using (var conn = new NpgsqlConnection(admin))
            {
                await conn.OpenAsync();
                // A template left over from an earlier run against the same external server is rebuilt, so the
                // template always matches the migrations in THIS checkout.
                await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{TemplateName}\" WITH (FORCE)", conn))
                    await drop.ExecuteNonQueryAsync();
                await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{TemplateName}\"", conn))
                    await create.ExecuteNonQueryAsync();
            }

            var templateCs = new NpgsqlConnectionStringBuilder(admin) { Database = TemplateName, Pooling = false }.ConnectionString;
            await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(templateCs).Options))
                await db.Database.MigrateAsync();

            // CREATE DATABASE … TEMPLATE refuses while anybody is connected to the template.
            NpgsqlConnection.ClearAllPools();

            _adminConnection = admin;
            return admin;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string> StartServerAsync()
    {
        var external = Environment.GetEnvironmentVariable("QRLOG_IT_CONNECTION");
        if (!string.IsNullOrWhiteSpace(external))
            return external;

        // The same major version production runs (docker-compose.prod.yml: postgres:16-alpine).
        var image = Environment.GetEnvironmentVariable("QRLOG_IT_PG_IMAGE");
        _container = new PostgreSqlBuilder()
            .WithImage(string.IsNullOrWhiteSpace(image) ? "postgres:16-alpine" : image)
            .Build();
        await _container.StartAsync();

        // Testcontainers' reaper removes the container when the run ends; this covers runs where the reaper is
        // switched off (TESTCONTAINERS_RYUK_DISABLED=true), so no database is left behind either way.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { _container.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { /* best effort */ }
        };

        return _container.GetConnectionString();
    }
}
