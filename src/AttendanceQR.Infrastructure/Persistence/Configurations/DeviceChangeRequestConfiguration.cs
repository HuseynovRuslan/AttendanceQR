using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class DeviceChangeRequestConfiguration : IEntityTypeConfiguration<DeviceChangeRequest>
{
    public void Configure(EntityTypeBuilder<DeviceChangeRequest> builder)
    {
        builder.ToTable("DeviceChangeRequests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.NewDeviceFingerprint)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(r => r.Status)
            .HasConversion<int>();

        // Requester (required). Restrict to preserve request history.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reviewer (optional). Restrict; keeps the record even if the reviewer is removed.
        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.ReviewedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
