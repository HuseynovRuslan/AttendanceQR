using AttendanceQR.Api;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Every row that can hold a person or a branch in place has a decided answer in the delete.
///
/// The employee delete knew three tables. Role assignments and PIN-reset requests arrived later with
/// Restrict keys to Employee; nobody told the delete, the in-memory provider enforces no keys, and every
/// delete of a former manager or of anyone who had pressed «PIN-i unutdum» was a 500 in production — a
/// forced one AFTER the person's attendance had been wiped. The branch delete had the same blind spot for
/// role grants, approval areas and approval requests.
///
/// These read the keys out of the model, so the next table with a Restrict key to Employee or Location
/// fails here, by name, instead of on an admin's screen. The migration snapshot is checked too: it is what
/// the database was built from.
/// </summary>
public class DeleteRulesTests
{
    private static AppDbContext NpgsqlContext()
    {
        var tenant = new TenantContext();
        tenant.Resolve(Guid.NewGuid());
        // Never connected to — the model and the migrations assembly need no server.
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=none;Username=none;Password=none")
            .Options, tenant);
    }

    private static List<string> Names(IModel model, Type principal)
        => RowReferences.Holding(model, principal).Select(r => r.Name).ToList();

    [Fact]
    public void Every_key_that_holds_an_employee_has_a_rule_and_every_rule_still_has_its_key()
    {
        using var db = NpgsqlContext();
        var holding = Names(db.Model, typeof(Employee));

        // A new table: decide whether it is history (behind «TAM silinsin»), the person's own (goes with
        // them) or someone else's that only names them (cleared) — in DeleteRules.Employee.
        Assert.Empty(holding.Except(DeleteRules.Employee.Keys));
        // A rule for a key that is gone, or now cascades: it would be dead code that reads like a guarantee.
        Assert.Empty(DeleteRules.Employee.Keys.Except(holding));
    }

    [Fact]
    public void Every_key_that_holds_a_branch_is_answered_by_the_branch_delete()
    {
        using var db = NpgsqlContext();
        var holding = Names(db.Model, typeof(Location));

        Assert.Empty(holding.Except(DeleteRules.Location));
        Assert.Empty(DeleteRules.Location.Except(holding));
    }

    [Fact]
    public void Only_a_column_that_may_be_empty_is_ever_cleared()
    {
        using var db = NpgsqlContext();
        foreach (var reference in RowReferences.Holding(db.Model, typeof(Employee)))
        {
            if (DeleteRules.Employee[reference.Name].Action == DeleteRules.Action.Detach)
                Assert.True(reference.Nullable, $"{reference.Name} is required — it can be removed, not cleared");
        }
    }

    [Fact]
    public void The_keys_found_are_the_ones_the_database_was_built_with()
    {
        // The code reads the running model; Postgres enforces what the migrations created. If the two ever
        // disagreed (a delete rule changed without a migration), the rules above would be checked against
        // the wrong list — so the snapshot must find exactly the same keys.
        using var db = NpgsqlContext();
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;

        foreach (var principal in new[] { typeof(Employee), typeof(Location) })
        {
            Assert.Equal(Names(db.Model, principal), SnapshotHolding(snapshot, principal.FullName!));
        }
    }

    [Fact]
    public void A_cascading_key_is_not_reported_as_holding()
    {
        // DeviceBinding and ManagedLocations cascade, AuditLogs are set null: the database handles those
        // itself, and listing them would put a question in front of the admin that nobody needs answered.
        using var db = NpgsqlContext();
        var holding = Names(db.Model, typeof(Employee));
        Assert.DoesNotContain("DeviceBinding.EmployeeId", holding);
        Assert.DoesNotContain("ManagedLocation.EmployeeId", holding);
        Assert.DoesNotContain("AuditLog.EmployeeId", holding);
        Assert.Contains("RoleAssignment.EmployeeId", holding);
    }

    /// <summary>The same question asked of the migration snapshot, whose entity types are known by name only.</summary>
    private static List<string> SnapshotHolding(IModel model, string principalName)
    {
        var principal = model.FindEntityType(principalName)!;
        var id = principal.FindPrimaryKey()!.Properties.Single().Name;
        return model.GetEntityTypes()
            .SelectMany(t => t.GetDeclaredForeignKeys())
            .Where(fk => fk.PrincipalEntityType == principal)
            .Where(fk => fk.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull))
            .Select(fk =>
            {
                var i = fk.PrincipalKey.Properties.ToList().FindIndex(p => p.Name == id);
                return $"{fk.DeclaringEntityType.Name.Split('.').Last()}.{fk.Properties[i].Name}";
            })
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }
}
