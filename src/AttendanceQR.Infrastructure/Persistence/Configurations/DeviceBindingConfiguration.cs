using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class DeviceBindingConfiguration : IEntityTypeConfiguration<DeviceBinding>
{
    public void Configure(EntityTypeBuilder<DeviceBinding> builder)
    {
        builder.ToTable("DeviceBindings");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.DeviceFingerprint)
            .IsRequired()
            .HasMaxLength(512);

        // 1-to-1: Employee.DeviceBinding <-> DeviceBinding.EmployeeId.
        // HasForeignKey<DeviceBinding> makes EmployeeId the (unique) dependent FK.
        builder.HasOne<Employee>()
            .WithOne(e => e.DeviceBinding)
            .HasForeignKey<DeviceBinding>(d => d.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Explicit for clarity — 1-to-1 already enforces uniqueness on the FK.
        builder.HasIndex(d => d.EmployeeId)
            .IsUnique();
    }
}
