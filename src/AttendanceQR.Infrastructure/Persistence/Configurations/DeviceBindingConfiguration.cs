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

        builder.Property(d => d.DeviceLabel)
            .HasMaxLength(100);

        builder.Property(d => d.LastSeenAtUtc)
            .IsRequired();

        // 1-to-many: an employee holds one binding per browser storage context.
        builder.HasOne<Employee>()
            .WithMany(e => e.DeviceBindings)
            .HasForeignKey(d => d.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per (employee, context) — including evicted ones, which are kept with
        // IsActive=false and reactivated if that context ever comes back.
        builder.HasIndex(d => new { d.EmployeeId, d.DeviceFingerprint })
            .IsUnique();
    }
}
