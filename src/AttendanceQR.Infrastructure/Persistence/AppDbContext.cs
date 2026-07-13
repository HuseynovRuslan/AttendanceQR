using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each entity has its own IEntityTypeConfiguration<T> in Persistence/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Multi-tenancy (Phase 0): every tenant-scoped entity carries TenantId → Tenant. Configured
        // centrally so all rows stay consistent. Query filters + auto-stamping arrive in Phase 1;
        // Restrict on delete so a tenant with data can't be removed out from under its rows.
        var tenantScoped = new[]
        {
            typeof(Employee), typeof(Location), typeof(AttendanceRecord), typeof(DeviceBinding),
            typeof(DeviceChangeRequest), typeof(MissedCheckoutRequest), typeof(DailySummary),
            typeof(AuditLog), typeof(ManagedLocation), typeof(NonWorkingDay), typeof(LeaveRecord),
        };
        foreach (var t in tenantScoped)
        {
            modelBuilder.Entity(t)
                .HasOne(typeof(Tenant)).WithMany()
                .HasForeignKey(nameof(Tenant) + "Id")
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity(t).HasIndex(nameof(Tenant) + "Id");
        }
    }
}
