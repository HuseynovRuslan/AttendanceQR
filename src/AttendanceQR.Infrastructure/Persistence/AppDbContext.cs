using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ITenantContext? _tenant;

    // ITenantContext is optional so the design-time factory (migrations) can build the model without
    // the DI container. At runtime it's always injected.
    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null)
        : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>The tenant every query is filtered to and every insert is stamped with. Referenced by
    /// the query filters below — EF re-reads it per query, so it tracks the current request's tenant.
    /// FAIL-CLOSED: throws when no tenant is resolved (or, design-time, when there is no context at
    /// all) instead of defaulting to tenant #1 — see <see cref="ITenantContext.TenantId"/>. Only the
    /// design-time factory constructs this without a context, and that path builds the model without
    /// ever executing a query, so it never reads this.</summary>
    public Guid CurrentTenantId => _tenant is not null
        ? _tenant.TenantId
        : throw new InvalidOperationException(
            "AppDbContext was constructed without an ITenantContext (design-time factory) and something " +
            "tried to run a tenant-scoped query or save through it.");

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<DeviceBinding> DeviceBindings => Set<DeviceBinding>();
    public DbSet<DeviceChangeRequest> DeviceChangeRequests => Set<DeviceChangeRequest>();
    public DbSet<MissedCheckoutRequest> MissedCheckoutRequests => Set<MissedCheckoutRequest>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<ManagedLocation> ManagedLocations => Set<ManagedLocation>();
    public DbSet<NonWorkingDay> NonWorkingDays => Set<NonWorkingDay>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<ProcessedScan> ProcessedScans => Set<ProcessedScan>();
    public DbSet<Announcement> Announcements => Set<Announcement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each entity has its own IEntityTypeConfiguration<T> in Persistence/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Multi-tenancy: every tenant-scoped entity carries TenantId → Tenant (FK + index configured
        // centrally). Delete is Restrict so a tenant with data can't be removed out from under its rows.
        var tenantScoped = new[]
        {
            typeof(Employee), typeof(Location), typeof(AttendanceRecord), typeof(DeviceBinding),
            typeof(DeviceChangeRequest), typeof(MissedCheckoutRequest), typeof(DailySummary),
            typeof(AuditLog), typeof(ManagedLocation), typeof(NonWorkingDay), typeof(LeaveRecord),
            typeof(Schedule), typeof(ProcessedScan), typeof(Announcement),
        };
        foreach (var t in tenantScoped)
        {
            modelBuilder.Entity(t)
                .HasOne(typeof(Tenant)).WithMany()
                .HasForeignKey(nameof(Tenant) + "Id")
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity(t).HasIndex(nameof(Tenant) + "Id");
        }

        // Phase 1 — the isolation boundary. Every query is automatically scoped to CurrentTenantId, so
        // one tenant can never read another's rows even if a query forgets to filter. Explicit per
        // entity (typed) so EF reliably treats CurrentTenantId as a runtime value, not a baked constant.
        modelBuilder.Entity<Employee>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Location>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AttendanceRecord>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<DeviceBinding>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<DeviceChangeRequest>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<MissedCheckoutRequest>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<DailySummary>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ManagedLocation>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<NonWorkingDay>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<LeaveRecord>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Schedule>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<ProcessedScan>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
        modelBuilder.Entity<Announcement>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

        // Idempotency key: a client scan id is processed at most once per tenant. The unique index is
        // what makes a replayed offline scan a no-op instead of a duplicate check-in.
        modelBuilder.Entity<ProcessedScan>().HasIndex(p => new { p.TenantId, p.ClientScanId }).IsUnique();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    // New tenant-scoped rows get the current tenant automatically (unless already set explicitly), so
    // callers never have to remember TenantId. This is what lets the column default be dropped.
    // CurrentTenantId is read lazily — it now throws when unresolved, and a save that stamps nothing
    // (updating existing rows, or writing the un-scoped Tenants registry itself) has no tenant to need.
    private void StampTenant()
    {
        var pending = ChangeTracker.Entries<ITenantScoped>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == Guid.Empty)
            .ToList();
        if (pending.Count == 0)
            return;

        var tenantId = CurrentTenantId;
        foreach (var entry in pending)
            entry.Entity.TenantId = tenantId;
    }
}
