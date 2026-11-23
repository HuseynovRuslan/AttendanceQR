using AttendanceQR.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.IntegrationTests.Baseline;

/// <summary>
/// Production applies migrations to itself at startup (Program.cs, <c>Database.Migrate()</c>), so a migration that
/// does not apply cleanly to PostgreSQL is an outage at the next deploy — and nothing ran them against a real
/// database before this. These pin: every migration applies, in order, to an empty PostgreSQL 16; nothing is left
/// pending; and the C# model has no change that a migration was never written for.
/// </summary>
public class MigrationsTests
{
    [PostgresFact]
    public async Task Every_migration_applies_to_an_empty_PostgreSQL_16_and_nothing_is_pending()
    {
        var cs = await PostgresServer.CreateDatabaseAsync();
        await using var db = Seed.Context(cs);

        var all = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();

        Assert.NotEmpty(all);
        Assert.Equal(all, applied);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var version = await db.Database.SqlQuery<string>($"SELECT current_setting('server_version') AS \"Value\"").SingleAsync();
        Assert.StartsWith("16", version);
    }

    [PostgresFact]
    public async Task The_model_has_no_change_without_a_migration()
    {
        var cs = await PostgresServer.CreateDatabaseAsync();
        await using var db = Seed.Context(cs);

        Assert.False(db.Database.HasPendingModelChanges(),
            "The EF model differs from the last migration snapshot — add a migration (dotnet ef migrations add …).");
    }

    [PostgresFact]
    public async Task The_migrations_insert_the_original_tenant_the_rest_of_the_code_assumes()
    {
        var cs = await PostgresServer.CreateDatabaseAsync();
        await using var db = Seed.Context(cs);

        var original = await db.Tenants.SingleAsync(t => t.Id == Seed.OriginalTenantId);
        Assert.Equal(Seed.OriginalTenantSlug, original.Slug);
        Assert.True(original.IsActive);
    }
}
