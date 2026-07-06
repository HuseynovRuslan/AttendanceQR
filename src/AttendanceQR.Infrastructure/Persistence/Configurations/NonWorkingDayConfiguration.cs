using AttendanceQR.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AttendanceQR.Infrastructure.Persistence.Configurations;

public class NonWorkingDayConfiguration : IEntityTypeConfiguration<NonWorkingDay>
{
    public void Configure(EntityTypeBuilder<NonWorkingDay> builder)
    {
        builder.ToTable("NonWorkingDays");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Description)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(n => new { n.Date, n.LocationId });

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(n => n.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
