using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class ManagedLocationConfiguration : IEntityTypeConfiguration<ManagedLocation>
{
    public void Configure(EntityTypeBuilder<ManagedLocation> builder)
    {
        builder.ToTable("ManagedLocations");

        // Composite key — a manager/location pair is unique by definition.
        builder.HasKey(m => new { m.EmployeeId, m.LocationId });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(m => m.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(m => m.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
