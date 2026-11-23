using AttendanceQR.Domain;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Multitenancy;
using AttendanceQR.Infrastructure.Persistence;
using AttendanceQR.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.IntegrationTests.Infrastructure;

/// <summary>
/// Writes test rows straight into a test database, the way production rows look — through the real
/// <see cref="AppDbContext"/> (so TenantId stamping and the query filters are the production ones), not raw SQL.
/// </summary>
public static class Seed
{
    /// <summary>The tenant the migrations themselves insert (Bakı Abadlıq). Physically the first row of Tenants.</summary>
    public static readonly Guid OriginalTenantId = TenantDefaults.BakiAbadligId;
    public const string OriginalTenantSlug = "bax";

    public static AppDbContext Context(string connectionString, Guid? tenantId = null)
    {
        var tenant = new TenantContext();
        if (tenantId is Guid id)
            tenant.Resolve(id);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options, tenant);
    }

    public static async Task<Guid> TenantAsync(string cs, string slug, decimal? priceOverride = null, string? plan = null,
        DateTime? trialEndsAtUtc = null)
    {
        await using var db = Context(cs);
        var tenant = new Tenant
        {
            Name = slug, Slug = slug, DisplayName = slug.ToUpperInvariant(), IsActive = true,
            Plan = plan, MonthlyPriceOverride = priceOverride, TrialEndsAtUtc = trialEndsAtUtc,
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>Sets billing fields on an EXISTING tenant row (used for the migration-seeded original tenant).</summary>
    public static async Task SetBillingAsync(string cs, Guid tenantId, decimal? priceOverride, string? plan)
    {
        await using var db = Context(cs);
        var t = await db.Tenants.SingleAsync(x => x.Id == tenantId);
        t.MonthlyPriceOverride = priceOverride;
        t.Plan = plan;
        await db.SaveChangesAsync();
    }

    public static async Task InvoiceAsync(string cs, Guid tenantId, int year, int month, decimal amount, int employees,
        string note, bool paid = true)
    {
        await using var db = Context(cs);
        db.TenantInvoices.Add(new TenantInvoice
        {
            TenantId = tenantId, PeriodYear = year, PeriodMonth = month, Amount = amount, EmployeeCount = employees,
            IsPaid = paid, PaidAtUtc = paid ? DateTime.UtcNow : null, Note = note,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public static async Task<Guid> LocationAsync(string cs, Guid tenantId, string name = "Baş ofis")
    {
        await using var db = Context(cs, tenantId);
        var location = new Location
        {
            Name = name, Latitude = 40.4093, Longitude = 49.8671, RadiusMeters = 150,
            ShiftStart = new TimeOnly(9, 0), ShiftEnd = new TimeOnly(18, 0), LateThresholdMinutes = 15,
        };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    /// <summary>An activated account that can sign in with <paramref name="pin"/> on its company's subdomain.</summary>
    public static async Task<Guid> EmployeeAsync(string cs, Guid tenantId, Guid locationId, string email, string pin,
        EmployeeRole role = EmployeeRole.Employee, Guid? id = null, bool allDayHours = false)
    {
        await using var db = Context(cs, tenantId);
        var employee = new Employee
        {
            Id = id ?? Guid.NewGuid(),
            FullName = email, Email = email, Role = role, LocationId = locationId,
            PasswordHash = new PasswordHasher().Hash(pin), IsActive = true,
            ActivatedAtUtc = DateTime.UtcNow.AddDays(-60), CreatedAtUtc = DateTime.UtcNow.AddDays(-60),
        };
        // A day shift spanning the whole calendar day, so a scan test behaves the same whatever hour the suite
        // runs at (no night-shift or early-morning rules come into play).
        if (allDayHours)
        {
            employee.WorkStart = new TimeOnly(0, 0);
            employee.WorkEnd = new TimeOnly(23, 59);
        }
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    /// <summary>An already-bound, active device — the state of a phone that has scanned before.</summary>
    public static async Task DeviceAsync(string cs, Guid tenantId, Guid employeeId, string fingerprint)
    {
        await using var db = Context(cs, tenantId);
        db.DeviceBindings.Add(new DeviceBinding
        {
            EmployeeId = employeeId, DeviceFingerprint = fingerprint, DeviceLabel = "test",
            BoundAtUtc = DateTime.UtcNow.AddDays(-30), LastSeenAtUtc = DateTime.UtcNow.AddDays(-1), IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    public static async Task ManagesAsync(string cs, Guid tenantId, Guid managerId, Guid locationId)
    {
        await using var db = Context(cs, tenantId);
        db.ManagedLocations.Add(new ManagedLocation { EmployeeId = managerId, LocationId = locationId });
        await db.SaveChangesAsync();
    }

    public static async Task<(int Total, int Open)> AttendanceCountsAsync(string cs, Guid tenantId, Guid employeeId)
    {
        await using var db = Context(cs, tenantId);
        var total = await db.AttendanceRecords.CountAsync(r => r.EmployeeId == employeeId);
        var open = await db.AttendanceRecords.CountAsync(r => r.EmployeeId == employeeId && r.CheckOutAtUtc == null);
        return (total, open);
    }

    /// <summary>Moves an employee's open check-in into the past, so the next scan reads as a real check-out.</summary>
    public static async Task BackdateOpenCheckInAsync(string cs, Guid employeeId, TimeSpan by)
    {
        await using var db = Context(cs);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AttendanceRecords\" SET \"CheckInAtUtc\" = \"CheckInAtUtc\" - {by} WHERE \"EmployeeId\" = {employeeId} AND \"CheckOutAtUtc\" IS NULL");
    }

    /// <summary>Rewrites a tenant row in place. PostgreSQL writes an updated row as a NEW tuple at the end of the
    /// heap, so this moves the row's physical position — what an unordered "first row" query would return.</summary>
    public static async Task TouchTenantAsync(string cs, Guid tenantId)
    {
        await using var db = Context(cs);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Tenants\" SET \"DisplayName\" = \"DisplayName\" WHERE \"Id\" = {tenantId}");
    }

    /// <summary>The id of the tenant row PostgreSQL returns first without an ORDER BY — i.e. heap order.</summary>
    public static async Task<Guid> PhysicallyFirstTenantAsync(string cs)
    {
        await using var db = Context(cs);
        return await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"Tenants\" LIMIT 1")
            .SingleAsync();
    }
}
