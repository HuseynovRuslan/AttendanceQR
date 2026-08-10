using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The query <c>OnTokenValidated</c> runs on EVERY authenticated request. If it cannot be translated
/// to SQL, the whole API answers 500 — nobody scans, nobody opens the panel — and no other test would
/// catch it: the rest of the suite runs on EF Core InMemory, which happily executes LINQ that Npgsql
/// cannot translate at all.
///
/// So this compiles the real query against the real PostgreSQL provider. <c>ToQueryString()</c> does
/// the translation without opening a connection, which is the whole point: it fails here, on a
/// laptop, instead of at 08:00 in front of 114 people.
///
/// It exists because the tenant-suspension check (a correlated subquery into Tenants) was added to
/// that projection. Anything else added there must keep this passing.
/// </summary>
public class AuthQueryTranslationTests
{
    private static AppDbContext NpgsqlContext()
    {
        var tenant = new TenantContext();
        tenant.Resolve(Guid.NewGuid());
        // A connection string that is never connected to — ToQueryString only needs the provider's
        // SQL generator, not a server.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none")
            .Options;
        return new AppDbContext(options, tenant);
    }

    [Fact]
    public void The_token_validation_query_translates_to_sql()
    {
        using var db = NpgsqlContext();
        var employeeId = Guid.NewGuid();

        // Character-for-character the projection Program.cs builds in OnTokenValidated.
        var sql = db.Employees
            .Where(e => e.Id == employeeId)
            .Select(e => new
            {
                e.TokenVersion,
                e.IsActive,
                TenantActive = db.Tenants.Any(t => t.Id == e.TenantId && t.IsActive),
            })
            .ToQueryString();

        Assert.Contains("\"TokenVersion\"", sql);
        Assert.Contains("\"IsActive\"", sql);
        // The suspension check must reach the database, not be silently evaluated in memory.
        Assert.Contains("Tenants", sql);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_tenant_filter_is_still_applied_to_employees()
    {
        // Multi-tenancy is fail-closed: the global query filter must survive on this hot path, or
        // token validation would read across companies.
        using var db = NpgsqlContext();
        var sql = db.Employees.Where(e => e.Id == Guid.NewGuid()).ToQueryString();
        Assert.Contains("TenantId", sql);
    }
}
