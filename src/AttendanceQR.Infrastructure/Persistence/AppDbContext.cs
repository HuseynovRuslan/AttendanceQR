using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceQR.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<DeviceBinding> DeviceBindings => Set<DeviceBinding>();
    public DbSet<DeviceChangeRequest> DeviceChangeRequests => Set<DeviceChangeRequest>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<ManagedLocation> ManagedLocations => Set<ManagedLocation>();
    public DbSet<NonWorkingDay> NonWorkingDays => Set<NonWorkingDay>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Each entity has its own IEntityTypeConfiguration<T> in Persistence/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
